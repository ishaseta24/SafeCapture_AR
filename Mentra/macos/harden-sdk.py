#!/usr/bin/env python3
"""Suppress credential-bearing SDK diagnostics, not transport or firmware logic.

Only the exact approved upstream source or our deterministic patched result is
accepted. This is a local build overlay, never a replacement release or a commit.
"""
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent / ".build/checkouts/mentra-bluetooth-sdk-ios/ios/Source"
SINKS = [
    ("Bridge.swift", "05350377bff672dc818d57f7e8c69d64e96f52e28dd76dcccaed9eef4b4f5cab",
     "    static func log(_ message: String) {"),
    ("sgcs/MentraLive.swift", "b4dbe559465be23f4646b9ace4e5586dfe0f4ffeb1d657782caa64782f5ffe01",
     "    private func transportLog(_ message: String, bridgeLogging: Bool) {"),
    ("Internal/BleTraceLogger.swift", "fa5d1e04d4312b193f8073c965ecd24d2bd3ed85edbdbddb4b56b14feea4e60f",
     "    private static func emit(_ line: String) {"),
]


PREFIX = "\n        #if !SAFE_CAPTURE_PRIVATE_DIAGNOSTICS"
SUFFIX = "\n        #endif"


def function_body(source, signature):
    if source.count(signature) != 1:
        raise SystemExit("Refusing ambiguous SDK diagnostic function")
    start = source.index(signature) + len(signature)
    # Hash guards freeze the known functions' indentation and closing brace.
    end = source.index("\n    }", start)
    return start, end


def original_source(source, signature):
    start, end = function_body(source, signature)
    body = source[start:end]
    # Only unwrap the block belonging to this function. Other #endif lines in
    # the SDK must not be removed while reconstructing its original checksum.
    if body.startswith(PREFIX) and body.endswith(SUFFIX):
        return source[:start] + body[len(PREFIX):-len(SUFFIX)] + source[end:]
    return source


def patched(source, signature):
    start, end = function_body(source, signature)
    return source[:start] + PREFIX + source[start:end] + SUFFIX + source[end:]


def harden(root=ROOT):
    pending = []
    for relative, digest, signature in SINKS:
        target = root / relative
        source = target.read_text()
        original = original_source(source, signature)
        if hashlib.sha256(original.encode()).hexdigest() != digest:
            raise SystemExit(f"Refusing unknown SDK source: {relative}; expected exact 3.3.0")
        result = patched(original, signature)
        if source != result:
            pending.append((target, result))
    for target, result in pending:
        # SwiftPM checks out read-only source. Only these hash-verified sink files change.
        target.chmod(target.stat().st_mode | 0o200)
        target.write_text(result)
    print("Exact SDK 3.3.0 diagnostic sinks hardened (idempotent).")


if __name__ == "__main__":
    harden()
