---
title: 'Direct Mentra Live to Mac capture'
type: 'feature'
created: '2026-11-14'
status: 'in-progress'
baseline_commit: '1475fd40913a39eb24677d18469f669aca74a973'
context:
  - '{project-root}/CLAUDE.md'
  - '{project-root}/Mentra/README.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Connect Mentra Live directly to a MacBook without iOS or
changes to Ray-Ban.

**Approach:** Build `Mentra/macos/`, a native Bluetooth controller, and
`Mentra/laptop/`, a local photo-ingest, YOLO, selection, and masking service.
Validate a single photo, then serialized snapshots. Continuous video follows later.

## Boundaries & Constraints

**Always:** Implement inside `Mentra/`; preserve Ray-Ban, presentation, Quest,
and relay. Use independent packages, port `8091`, and the shared model read-only.
Authenticate before buffering; bound bytes, pixels, concurrency, and viewer
queues. Mask before output; preserve selections on failure. Keep camera buffers
in memory, with no raw-image route. Test synthetic images. Disable SDK analytics.

**Ask First:** Modifying existing integrations, cloud image transport, public
deployment, installing firmware, committing, or pushing.

**Never:** Runtime-import Ray-Ban; read secrets; disable capture indicators;
auto-update firmware; require iOS; claim universal redaction or offline SDK use.

## I/O & Edge Cases

- Disconnected, unready, or compatibility unknown: disable capture; explain recovery.
- Authorized multipart `POST /mentra/photo` with JPEG `photo` and bounded,
  nonblank `requestId`: process, then acknowledge that ID without raw URLs.
- Bad token: `401` before parsing. Missing/duplicate fields, malformed
  multipart, or corrupt JPEG: `400`. Non-JPEG: `415`. Byte/pixel limit: `413`.
- Busy: `503`, no raw-frame queue. Abort/error always releases capacity.
- Lost selection, stale detection, dimension change, or processing failure:
  preserve masks; fully mask or withhold unsafe output.
- Stop/disconnect/sleep: issue no further captures; surface any request
  already in flight rather than promising it can be retracted.
- No photos: report stale status.

</frozen-after-approval>

## Code Map

- `RayBan/laptop/src/{server,vision,postprocess,privacy}.js` — copy/reference
  pipeline; harden only the Mentra copy.
- `Assets/SafeCapture/Models/yolov8n.onnx` — read-only COCO detector.
- `Mentra/macos/` — new SwiftPM AppKit application.
- `Mentra/laptop/` — new independent Node service.

## Tasks & Acceptance

**Execution:**

- [x] `Mentra/macos/Package.swift` — pin SDK `3.3.0` per the subsequent user
  change; initial `3.4.0` build evidence is retained below.
- [ ] `Mentra/laptop/{package.json,package-lock.json,.env.example}` —
  independent install/start/test scripts, port, limits, and placeholder token.
- [ ] `Mentra/laptop/src/{vision,postprocess,privacy}.js` — copy pipeline;
  harden rendering-error and geometry fallbacks.
- [ ] `Mentra/laptop/src/{server,photo-upload}.js` — authenticated bounded
  webhook, health/status, processed viewer, token-protected selection mutations.
- [ ] `Mentra/macos/Sources/SafeCaptureMentra/GlassesController.swift` —
  own SDK, device selection, readiness/compatibility checks, Wi-Fi setup
  without stored credentials, and serialized authenticated `requestPhoto`
  with gallery saving disabled.
- [ ] `Mentra/macos/Sources/SafeCaptureMentra/App.swift` — provide device
  picker/status, LAN webhook/token inputs, capture/start/stop, lifecycle
  cleanup, and errors; no unmasked preview.
- [ ] `Mentra/macos/{Info.plist,SafeCaptureMentra.entitlements,build.sh}` —
  bundle/sign `.app` with Bluetooth/network permissions; no microphone capture.
- [ ] `Mentra/laptop/test/`, `Mentra/macos/Tests/` — add node:test and Swift
  tests for all edge cases, privacy boundaries, configuration, and capture lifecycle.
- [ ] `Mentra/README.md` — verified setup, matching firmware, limits, and evidence.

**Acceptance Criteria:**

- Given Ray-Ban exists, when Mentra runs on `8091`, then its files and
  dependencies remain unchanged and default ports do not conflict.
- Given compatible Mentra Live and a permitted Mac, when the user connects
  and captures, then a processed snapshot appears without any phone.
- Given a selected detection, when another snapshot arrives or processing
  fails, then masks apply before output or unsafe output is withheld.
- Given capture is active, when stopped or the Mac sleeps, then no new
  capture request starts until the user explicitly resumes.
- Given no glasses or secrets, when tests run, then upload, privacy, and
  controller logic can be verified.

## Spec Change Log

2026-11-14: The user requested SDK 3.3.0 for glasses app 3.3.0 / build 303000422,
then explicitly requested skipping validation and going directly from Wi-Fi to
manual photo capture. This overrides the earlier compatibility-unknown capture
gate: no version/manifest check is required for admission. Connection, boot,
gallery-disable acknowledgement, transport readiness, receiver authentication,
and manual capture remain required. Firmware is not installed. The user also
instructed not to add tests; the follow-up adds no tests.
See `spec-mentra-sdk-3-3-0.md` for this scoped change and verification.

Additional user-requested follow-ups: retain confirmed gallery settings after
upload failures, surface safe error categories/HTTP statuses, and load private
`.env` defaults instead of retyping receiver settings. Persisted credentials
are explicitly authorized by that request and excluded from Git/app bundles.
The user-reported HTTP 400 exposed SDK multipart metadata (`type` and `success`)
that the original receiver rejected; allow only the exact supported metadata
values while keeping authentication and bounds unchanged. No tests were added.

## Design Notes

Historical initial build, 2026-11-14: SDK exact `3.4.0` resolved to
`0c7d9c1a9f9f9a4931ec995fd2a1673ee2c2327b` and the minimal native macOS
consumer built successfully with Swift 6.3.3 / Command Line Tools, without
consumer compatibility flags or SDK source changes.

Bluetooth controls capture; Wi-Fi sends JPEGs to the Mac LAN address, not
`localhost`. SDK `3.4.0` requires matching glasses software. Its public tag
declares macOS 13+; verify the build despite older development-only wording
in the macOS guide. Do not silently substitute an unpinned development SDK.

Next: local RTMP/SRT/WHIP receiver feeding this pipeline, with hardware
latency/backpressure tests. Snapshots cannot establish video performance.

Masking is opt-in; COCO does not detect arbitrary text/documents. Raw frames
reach the Mac before masking. HTTP requires a trusted LAN; TLS/public hosting
are separate work. The SDK still checks for updates with analytics disabled.
Sources: official guides and tagged source linked in `Mentra/README.md`.

## Verification

**Current 3.3.0 follow-up:** exact SDK dependency resolved and the native Mac
release bundle compiled and passed ad-hoc signature verification. The app was
opened for the user's manual Wi-Fi/photo check. No tests were retained or run,
as requested. Earlier test-command entries below are historical notes, not
verification of this follow-up. Physical photo capture remains unverified.

**Commands:**
- `npm --prefix Mentra/laptop ci` and `npm --prefix Mentra/laptop test` — pass.
- `swift test --package-path Mentra/macos` — controller decision tests pass.
- `bash Mentra/macos/build.sh --build-only` — bundled, ad-hoc-signed Mac app.
- `git diff --check` and changed-path review — existing integrations untouched.

**Hardware checks:**
- Pair Mac and Mentra Live; verify Wi-Fi capture, masks, bad-token handling,
  disconnect, stop, and sleep/wake without a phone.
- Mark hardware verification pending if glasses are unavailable.
