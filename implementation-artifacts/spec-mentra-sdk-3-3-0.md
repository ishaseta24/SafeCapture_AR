---
title: 'Match SafeCapture to Mentra Live 3.3.0'
type: 'chore'
created: '2026-11-14'
status: 'done'
baseline_commit: '1475fd40913a39eb24677d18469f669aca74a973'
context:
  - '{project-root}/CLAUDE.md'
  - '{project-root}/Mentra/README.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** SafeCapture uses Bluetooth SDK 3.4.0, but the glasses report app
3.3.0 / build 303000422. Release validation blocks capture.

**Approach:** Pin the Mac application to published SDK 3.3.0, update diagnostics
and instructions, and rebuild for a hardware retry. At the user's explicit
request, skip version validation: connect, provision Wi-Fi, and manually capture.

## Boundaries & Constraints

**Always:** Disable analytics and credential-bearing diagnostics. Retain
connection, boot, gallery-mode, transport, and capture-lifecycle gates. Use the
published 3.3.0 tag without overriding update URLs. Preserve unrelated
uncommitted work, snapshots, and receiver protocol. Work in place as authorized.

**Ask First:** Firmware installation; weakening safeguards; SDK transport fixes
or private forks; unrelated changes; commits, pushes, or branch switches.

**Never:** Auto-install firmware, read secrets, replace the
Mentra implementation wholesale, add tests (per user instruction), or claim
compilation proves camera operation.

## I/O & Edge Cases

- Build 303000422: use SDK 3.3.0, but do not gate capture on firmware matching.
  Explain that firmware compatibility is unverified and requests can fail.
- Ready glasses: automatically disable gallery capture once per session; a
  failed or unconfirmed acknowledgement requires explicit camera-setup retry.
- Connected Wi-Fi and confirmed camera setup: allow manual photo capture;
  never initiate a photo merely because the network connected.
- Repeated hardening: accept known source or its hardened result; unknown
  checksums fail before any writes.
- Running app: do not replace its executable; obtain authorization to quit.

</frozen-after-approval>

## Code Map

- `Mentra/macos/Package.swift` and `Package.resolved` — exact SDK dependency.
- `Mentra/macos/Sources/CaptureCore/CaptureState.swift` — required release and
  fail-closed capture readiness.
- `Mentra/macos/Sources/SafeCaptureMentra/{App,GlassesController}.swift` —
  automatic camera setup, manual retry, and version-check disclosure.
- `Mentra/macos/harden-sdk.py` — checksum-verified SDK diagnostic overlay.
- `Mentra/macos/Sources/SafeCaptureMentra/LocalConfiguration.swift` and
  `Mentra/macos/configure-env.py` — bounded private `.env` loading and setup.
- `Mentra/laptop/src/photo-upload.js` — accept the exact SDK upload metadata.
- `Mentra/macos/build.sh` — resolve, harden, compile, bundle, and sign.
- `Mentra/README.md` — setup and matching-release instructions.
- `implementation-artifacts/spec-mentra-local-capture.md` — integration record;
  append the authorized dependency change without rewriting history.

## Tasks & Acceptance

**Execution:**
- [x] `Mentra/macos/Package.swift`, `Package.resolved` — resolve exact 3.3.0 and
  verify the upstream tag commit.
- [x] `Mentra/macos/harden-sdk.py` — replace 3.4.0 source checksums with verified
  3.3.0 checksums and trace-sink signature; preserve idempotence and atomic checks.
- [x] `Mentra/macos/Sources/CaptureCore/CaptureState.swift` and
  `Mentra/macos/Sources/SafeCaptureMentra/{App,GlassesController}.swift` — use the
  selected release, remove firmware validation, and automatically prepare gallery
  settings without retry loops, stale-session success, or automatic captures.
- [x] `Mentra/README.md` and
  `implementation-artifacts/spec-mentra-local-capture.md` — document the switch,
  matching app build, checks, and pending hardware verification.
- [x] `Mentra/macos/build.sh` — exercise the existing build unchanged unless a
  demonstrated API difference requires a scoped consumer adjustment.
- [x] `Mentra/macos/Sources/SafeCaptureMentra/LocalConfiguration.swift`,
  `App.swift`, `.env.example`, and `configure-env.py` — create ignored private
  defaults, auto-load them on launch, and allow explicit reload without actions.
- [x] `Mentra/laptop/src/photo-upload.js` — accept the SDK's exact optional
  metadata while retaining auth and resource limits; restart the scoped service.
- [x] `Mentra/laptop/src/server.js` — display complete processed snapshots
  without single-frame MJPEG buffering; keep withholding and no-store behavior.

**Acceptance Criteria:**
- Given a fresh dependency resolution, when the Mac project resolves packages,
  then it selects SDK 3.3.0 at upstream commit
  `9dcdad415d7fe2fe133cc0c7279c6bc45659e17b`, not 3.4.0 or an unpinned branch.
- Given failed readiness or camera setup, when preparation ends, then capture
  stays disabled and explicit retry is available without a version check.
- Given known sources, when hardening runs twice, then output is identical;
  given unknown source, when hardening runs, then it fails without partial edits.
- Given hardened SDK 3.3.0, when the bundle build runs, then a signed app is
  produced without adding tests.
- Given connected ready glasses with camera setup confirmed, when Wi-Fi
  connects, then manual capture becomes available without a version check.
- Given a reconnect or camera-setup failure, when state updates arrive, then
  no automatic photo, endless setup retry, or stale-session admission occurs.
- Given unrelated work, when complete, then other integrations and the Mentra
  receiver remain untouched.

## Spec Change Log

2026-11-14: The user explicitly requested "skip the validation and go to the
photo part directly after wifi connection." This overrides the earlier
exact-firmware requirement. Retain SDK 3.3.0, authenticated manual capture,
gallery-disable acknowledgement, connection/transport gates, and privacy
hardening; remove manual and automatic firmware-version admission checks.

2026-11-14: The user instructed "dont add tests." Remove the newly created test
files and test target; do not run the proposed test suites. Verify by compilation,
signing, dependency inspection, and scoped source review only.

2026-11-14: Final edge-case review found that a photo finishing after BLE
reconnection could leave camera setup idle with no visible retry. Reconsider
one-time automatic preparation when capture busy clears, retaining the
per-session attempt guard and manual-only photos.

2026-11-14 follow-up: User reported repeated delivery failure after correcting
the receiver URL. Receiver health/model were ready, but processed-frame count
was zero. Preserve confirmed gallery settings after upload failure instead of
misreporting camera setup as unconfirmed; stop serial requests and allow explicit
manual retry. Translate SDK errors into fixed categories or numeric HTTP status
only, never exposing upstream descriptions. No tests added or run.

2026-11-14 follow-up: The user explicitly requested `.env` defaults to avoid
retyping configuration. This supersedes memory-only credential storage: create
an ignored project `.env` link to an owner-only sandbox configuration; reuse
the receiver's existing token without printing it. Load receiver settings and
optional Wi-Fi/USB defaults on launch or explicit reload. Do not execute shell
values, persist field edits automatically, or initiate connections/photos.
Keep the sandbox unchanged and credentials out of the app bundle. No tests.

2026-11-14 follow-up diagnosis: The user supplied the newly surfaced HTTP 400.
The exact ASG 3.3.0 source uploads `photo`, `requestId`, `type=photo_upload`,
and `success=true`; the receiver previously allowed only the first two parts.
Permit those exact optional metadata fields and raise only the bounded
field/part counts. Keep authentication, duplicate/unknown-field rejection,
byte/pixel limits, and all processing/privacy behavior. Restart the scoped
receiver to load this fix. Do not add tests or automatically take a photo.

2026-11-14 viewer follow-up: User saw a black preview despite a processed photo.
Receiver metadata showed one published frame, detections, no processing error,
and no full-frame mask. An in-memory read of the processed MJPEG part confirmed
a valid 1280x960 JPEG with normal brightness; no image was saved or printed.
Display complete processed JPEG responses via `/snapshot.jpg` instead of
depending on a browser decoding one open-ended MJPEG part. Preserve privacy
withholding, no-store responses, mutation-time hiding, stale selection checks,
and manual captures. No tests or new camera requests.

## Design Notes

SDK 3.3.0 declares macOS 13 support and builds unmodified on this Mac. Its
manifest selects glasses build 303000422, matching Device Info. The user chose
to skip firmware matching; neither that build label nor a successful build
proves physical capture compatibility.

The diagnostic overlay remains a local build step, not a transport patch.
SDK 3.3.0 changes `BleTraceLogger.emit`, requiring a verified signature update.

## Verification

**Commands:**
- `swift package --package-path Mentra/macos resolve` — exact 3.3.0 dependency.
- `python3 Mentra/macos/harden-sdk.py` twice — verified, idempotent overlay.
- `bash Mentra/macos/build.sh --build-only` — compile and signed app bundle.
- `git diff --check` and scoped review — no unrelated changes; account for
  pre-existing untracked directories.

**Hardware checks:**
- Disconnect the iPhone app, reconnect in the rebuilt Mac app, join Wi-Fi, and try
  one photo before serial snapshots. Require direct evidence to mark passed.

**Results:**
- SDK resolution selected exact 3.3.0 at the expected upstream commit.
- Diagnostic hardening succeeded twice; no firmware or transport code changed.
- Release app build and ad-hoc signature verification succeeded. The initial
  command timed out during SDK compilation; the longer build completed in 137s.
- Scoped source/lockfile review and whitespace checks passed.
- Blind and acceptance reviews found no actionable issues. The edge-case
  review's reconnect finding was fixed; the final app rebuilt and passed
  signature verification in 1.77s.
- No tests were retained or run, per user instruction.
- Physical Wi-Fi capture was subsequently confirmed by the user's
  "Photo delivered and processed" screenshot and one received/published frame.
  A valid, normally exposed processed JPEG was confirmed in memory; visual
  confirmation of the updated viewer is pending. USB/serial capture is unverified.
- Follow-up: the user reported HTTP 400. Exact SDK source confirmed the
  additional multipart metadata; the receiver contract was corrected and
  restarted gracefully while idle. Node syntax checks passed. Health reports
  the model loaded; no photo was triggered automatically.
- Follow-up: private `.env` was created using receiver settings without printing
  credentials. Its ignored project link targets the app sandbox; runtime mode
  is `0600`. Final config-enabled app compiled/signed in 2.70s and was opened.
- Receiver file-token authentication was confirmed with an intentionally invalid
  selection coordinate request, rejected before mutation. Mac firewall was
  disabled; prior glasses IP responded to ping. These diagnostic checks did not
  capture, upload, or expose an image or token.
- 2026-11-15: Complete processed JPEG endpoint and browser viewer update deployed.
  Node and embedded browser-script syntax checks passed. Receiver restarted
  gracefully while idle, clearing the old in-memory frame. Health/model ready;
  `/snapshot.jpg` correctly returns no-content before a new photo and no-store
  caching. No test files or automatic photo requests were added.

## Suggested Review Order

**Connection to manual capture**

- Automatic camera setup replaces firmware validation; photos remain explicitly requested.
  [`GlassesController.swift:172`](../Mentra/macos/Sources/SafeCaptureMentra/GlassesController.swift#L172)
- Reconsider setup when an interrupted photo releases the busy gate after reconnect.
  [`GlassesController.swift:67`](../Mentra/macos/Sources/SafeCaptureMentra/GlassesController.swift#L67)
- Require camera settings and transport readiness, not firmware matching.
  [`CaptureState.swift:104`](../Mentra/macos/Sources/CaptureCore/CaptureState.swift#L104)
- Display skipped verification honestly and offer camera-setup retry only on failure.
  [`App.swift:78`](../Mentra/macos/Sources/SafeCaptureMentra/App.swift#L78)

**Release and privacy**

- Keep credential-bearing SDK diagnostics suppressed using verified 3.3.0 source hashes.
  [`harden-sdk.py:49`](../Mentra/macos/harden-sdk.py#L49)
- Pin the published SDK release matching the user's installed glasses app.
  [`Package.swift:9`](../Mentra/macos/Package.swift#L9)
- Explain setup, limitations, and manual capture without firmware validation.
  [`README.md:45`](../Mentra/README.md#L45)
