#!/usr/bin/env python3
"""Create private Mac defaults without displaying or bundling credentials."""
import argparse
import ipaddress
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parent
RUNTIME = (
    Path.home() / "Library/Containers/local.safecapture.mentra/Data/Library"
    / "Application Support/SafeCaptureMentra/.env"
)
MAX_BYTES = 16 * 1024


def mac_ip():
    for interface in ("en0", "en1"):
        result = subprocess.run(
            ["ipconfig", "getifaddr", interface], capture_output=True, text=True, timeout=5
        )
        if result.returncode == 0:
            return result.stdout.strip()
    raise SystemExit("No Mac LAN address found. Supply --mac-ip with your private IPv4 address.")


def configure(address):
    try:
        ip = ipaddress.IPv4Address(address)
    except ipaddress.AddressValueError:
        raise SystemExit("Supply a private LAN IPv4 address.") from None
    if not (ip in ipaddress.IPv4Network("10.0.0.0/8")
            or ip in ipaddress.IPv4Network("172.16.0.0/12")
            or ip in ipaddress.IPv4Network("192.168.0.0/16")):
        raise SystemExit("Supply a private LAN IPv4 address.")
    local = ROOT / ".env"
    if os.path.lexists(local) and (
        not local.is_symlink() or local.resolve() != RUNTIME
    ):
        raise SystemExit("Existing Mentra/macos/.env left untouched. Move it aside before configuring.")
    if not RUNTIME.exists():
        receiver = ROOT.parent / "laptop/.env"
        contents = b""
        if receiver.exists():
            # Preserve the receiver's token privately, never through stdout.
            with receiver.open("rb") as stream:
                contents = stream.read(MAX_BYTES + 1)
        if len(contents) > MAX_BYTES:
            raise SystemExit("Receiver configuration is too large; no configuration was written.")
        defaults = {
            "SAFE_CAPTURE_WEBHOOK_URL": f"http://{ip}:8091/mentra/photo",
            "SAFE_CAPTURE_USB_WEBHOOK_URL": "http://127.0.0.1:8091/mentra/photo",
            "SAFE_CAPTURE_UPLOAD_MODE": "wifi",
            "SAFE_CAPTURE_ADB_SERIAL": "",
            "SAFE_CAPTURE_WIFI_SSID": "",
            "SAFE_CAPTURE_WIFI_PASSWORD": "",
        }
        if not re.search(rb"(?m)^\s*(?:export\s+)?SAFE_CAPTURE_INGEST_TOKEN\s*=", contents):
            defaults["SAFE_CAPTURE_INGEST_TOKEN"] = ""
        contents += b"\n# Mac app defaults. Private plaintext; never commit or share.\n"
        for key, value in defaults.items():
            if not re.search(rb"(?m)^\s*(?:export\s+)?" + key.encode() + rb"\s*=", contents):
                contents += f"{key}={value}\n".encode()
        if len(contents) > MAX_BYTES:
            raise SystemExit("Combined configuration is too large; no configuration was written.")
        RUNTIME.parent.mkdir(parents=True, mode=0o700, exist_ok=True)
        descriptor = os.open(RUNTIME, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(contents)
        print("Created private runtime .env using existing receiver settings where available.")
    else:
        print("Existing runtime .env preserved without changes.")
    if not os.path.lexists(local):
        local.symlink_to(RUNTIME)
    print("Edit: Mentra/macos/.env")
    print("The sandboxed Mac app reloads this file on launch or with Reload .env.")
    print("No credential values were printed. No connection or photo was initiated.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mac-ip", help="Private Mac IPv4 address; otherwise detect Wi-Fi.")
    arguments = parser.parse_args()
    configure(arguments.mac_ip or mac_ip())
