---
title: 'Make object selection work on the displayed snapshot'
type: 'bugfix'
created: '2026-11-15'
status: 'done'
baseline_commit: '1475fd40913a39eb24677d18469f669aca74a973'
context:
  - '{project-root}/CLAUDE.md'
  - '{project-root}/Mentra/README.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Capturing and viewing photos works, but clicking detected objects
leaves the selected count at zero. Selection expires after 15 seconds even for
an unchanged still photo, and status polling immediately overwrites errors.

**Approach:** Permit masking the currently displayed still image regardless of
age, while binding selection requests to that exact source photo. Keep action
errors visible so authentication, misses, and changed-frame rejection are clear.
Following explicit user approval, automatically mask detected TVs and laptops
on every new photo before publishing it to the browser.

## Boundaries & Constraints

**Always:** Authenticate selection changes. Preserve selected masks on failures,
full-frame fallback for lost/stale tracking across new captures, bounded image
processing, and processed-only output. Work only in Mentra and its scoped spec;
preserve existing uncommitted work and the working SDK 3.3.0/capture/.env setup.

**Ask First:** Changing mask opt-in behavior, storing photos, exposing raw
images, weakening cross-frame privacy fallback, modifying unrelated
integrations, committing, or pushing.

**Never:** Add or run tests or further review workflows, auto-select classes other than configured TVs/laptops, automatically capture photos,
clear masks to make an error disappear, log tokens, or claim browser behavior
was verified without a real browser observation.

## I/O & Edge Cases

- Old but unchanged displayed snapshot: valid object clicks can toggle masks;
  elapsed wall time alone does not invalidate coordinates on an immutable image.
- New photo arrives while the viewer loads or the token prompt is open: reject
  the old selection without modifying masks and refresh the displayed snapshot.
- Missing/mismatched frame identity: reject explicitly, including legacy tabs.
- Incorrect browser token, no object under the cursor, or busy processing:
  preserve the action error separately from the periodic receiver status.
- Inference/render errors or full-mask fallback: keep existing fail-closed
  behavior. A time gap across different photos still triggers tracking fallback.
- Automatic TV/laptop masks: derive only from each new photo's detections before
  its first publish; do not reuse auto-track identities from an older photo.
- Manual masks: retain existing cross-photo fail-closed behavior. Automatic
  masks cannot be removed by a manual toggle or manual-clear action.

</frozen-after-approval>

## Code Map

- `Mentra/laptop/src/pipeline.js` — source-photo identity, selection admission,
  retained masks, and tracking fallback.
- `Mentra/laptop/src/server.js` — selection route, processed snapshot response,
  displayed-image state, token prompt, and browser error feedback.
- `Mentra/laptop/src/privacy.js` — existing pixelation renderer; preserve it.
- `Mentra/README.md` — explain static snapshot selection and browser controls.

## Tasks & Acceptance

**Execution:**
- [x] `Mentra/laptop/src/pipeline.js` — assign a server-generated unique frame
  identity per incoming photo, expose it in status, clear it on explicit clear,
  and require it in selection. Replace only the age-based still-image check.
- [x] `Mentra/laptop/src/server.js` — pass the expected frame identity to
  selection; reject mismatched snapshot-image requests so metadata and pixels
  cannot silently refer to different photos.
- [x] `Mentra/laptop/src/server.js` — associate click coordinates with the image
  actually loaded, including before a token prompt. Keep action feedback visible
  separately from status polling; do not retry mutations automatically.
- [x] `Mentra/README.md` — document browser control-token entry, selectable old
  stills, changed-photo rejection, and unchanged cross-frame mask safeguards.
- [x] `Mentra/laptop/src/server.js` — deploy with a graceful idle receiver
  restart; warn that in-memory photos will be cleared. No Mac rebuild is needed.
- [x] `Mentra/laptop/src/pipeline.js` — derive per-photo automatic TV/laptop
  masks separately from persistent manual masks; render their union before
  publishing, preserving detection/render failure behavior.
- [x] `Mentra/laptop/src/server.js` — read validated automatic-mask classes,
  expose accurate effective selection/policy metadata, and explain manual versus
  automatic masks without requiring browser authentication merely to view.
- [x] `Mentra/laptop/.env.example` and `Mentra/README.md` — document the explicit
  `SAFE_CAPTURE_AUTO_MASK_CLASSES=tv,laptop` policy and its detection limits.
- [x] `Mentra/laptop/.env` — enable the requested local policy without printing,
  replacing, or exposing other credentials; restart the scoped idle receiver.

**Acceptance Criteria:**
- Given an unchanged displayed photo older than 15 seconds, when an authenticated
  user clicks a detected object, then a mask can be selected on that same photo.
- Given another source photo, when an old/missing frame identity is submitted,
  then selection fails with a clear conflict and existing masks are unchanged.
- Given a photo loading race or token prompt delay, when a click is submitted,
  then its frame identity comes from the displayed image, not latest status.
- Given a failed selection, when status polling continues, then its action
  error remains visible until the next deliberate action.
- Given selected objects and a later stale/lost track, when a new photo is
  processed, then existing full-mask/withhold behavior remains unchanged.
- Given the user prohibition on tests, when checks run, then only syntax/source
  checks and health checks run; no test files or automatic captures are added.
- Given detected TV/laptop objects on a new photo, when the authenticated upload
  is processed, then its first published browser image already has those masks.
- Given photos separated by a long interval and only automatic masks, when a new
  photo arrives, then stale automatic track IDs do not black out the new frame.
- Given manual selections, when automatic policy is active, then their original
  tracking fallback remains intact and manual clearing cannot disable policy.

## Spec Change Log

2026-11-15: The user instructed "don't do reviews and tests." Do not schedule
further review agents or test execution. Automatic masking is deployed and the
receiver reports the enabled TV/laptop policy; awaiting only user photo capture.

2026-11-15: The user explicitly approved automatic TV/laptop masking on every
new photo, so clicking Single photo should be sufficient to see masked output.
This authorizes class-based automatic selection only for the configured screen
classes and supersedes the earlier blanket prohibition on automatic selection.
Keep automatic masks per source photo, distinct from manually retained tracks.
Do not add tests, take photos automatically, or claim undetected screens are
redacted. The prior manual selection fix and frame-identity guards remain.

## Design Notes

The live receiver reported 14 detections, zero selections, no inference error,
and a photo age of 106 seconds. `select()` rejects images older than 15 seconds.
The renderer already pixelates selected regions; no renderer rewrite is needed.

Use a server-generated source-frame identifier, not the SDK request ID or the
published-render counter. Mask edits change rendered output but not source
coordinates. Every new source photo gets a distinct identity, including across
receiver restarts. Reject stale identities before changing selection state.

## Verification

- `npm --prefix Mentra/laptop run check` — Node syntax remains valid.
- Check the embedded viewer script with `node --check` through stdin; no files.
- Review identity propagation, persistent action errors, and untouched privacy
  fallback; inspect scoped changes without staging or exposing config files.
- Receiver health must recover after graceful restart. Ask the user to refresh,
  capture once, enter the control token, wait over 15 seconds, and select a box.
- User confirms selection count, cyan box, and visible pixelation. Keep this
  manual check pending until observed; no automatic photo or mask mutations.

Implementation verification (2026-11-15):
- Passed `npm --prefix Mentra/laptop run check`.
- Passed embedded viewer JavaScript syntax checking with `node --check` on stdin.
- Source review confirms identity admission precedes selection changes; loaded
  image identity and coordinates are captured before token entry. Polling does
  not write action feedback. Existing `process()` stale/lost-track fallback and
  `privacy.js` remain unchanged.
- No tests, browser/API mutations, captures, restarts, or commits performed.
  Deployment, receiver health, and manual browser confirmation remain pending
  with the parent session. Warn before restart: in-memory photos will be lost.

Deployment verification (parent, 2026-11-15):
- Restarted only the scoped idle receiver gracefully after warning about
  in-memory photo loss. Health reports the detector/model ready.
- Served status includes source-frame identity and no retained photo after
  restart. Served viewer includes persistent action feedback and frame-bound
  click handling. Existing pixelation-renderer checksum is unchanged.
- No tests, photo captures, selection mutations, secret reads, or commits.
- Manual browser confirmation of visible masking is still pending.

Review completion (2026-11-15):
- Blind, edge-case, and acceptance source reviews found no actionable issues.
- No code changes were needed after deployment. The receiver remains running.
- No tests or commits were made. Visible masking still needs user confirmation.

User-directed operation (2026-11-15):
- User explicitly requested masking the TV and screen. On the existing photo,
  authenticated frame-bound selection applied masks to detected `tv` and
  `laptop` regions. Receiver reports two selected objects, processed output
  available, and no rendering error.
- This was an explicit operation on the user's current photo, not automatic
  class masking. No photo was captured, no image saved, and no token printed.
- Browser click/authentication behavior and visual confirmation remain separate
  from the successful receiver-side selection.

Automatic-policy deployment (2026-11-15):
- Enabled `SAFE_CAPTURE_AUTO_MASK_CLASSES=tv,laptop` in the private receiver
  configuration, preserving other values without printing credentials.
- Restarted only the scoped idle receiver gracefully. Health/model are ready;
  live status confirms automatic classes `tv` and `laptop`. The Mac app and SDK
  were not rebuilt or restarted.
- The new viewer exposes the active policy and separate automatic/manual counts.
  No browser control token is needed merely to view automatic processed output.
- Syntax checks passed; the pixelation-renderer checksum is unchanged. No tests,
  photos, or automatic mask API actions were initiated. End-to-end confirmation
  awaits the user's next photo.

## Suggested Review Order

**Select the exact displayed still**

- Remove only wall-clock selection expiry; reject mismatched source photos before changing masks.
  [`pipeline.js:97`](../Mentra/laptop/src/pipeline.js#L97)
- Give every incoming source photo a distinct identity, independent of mask rerenders.
  [`pipeline.js:61`](../Mentra/laptop/src/pipeline.js#L61)
- Bind processed image delivery to its source identity, preventing metadata/image races.
  [`server.js:82`](../Mentra/laptop/src/server.js#L82)
- Capture displayed photo identity and coordinates before a token prompt can delay submission.
  [`server.js:154`](../Mentra/laptop/src/server.js#L154)

**Visible feedback and instructions**

- Keep action outcomes separate from polling; never automatically retry selection mutations.
  [`server.js:142`](../Mentra/laptop/src/server.js#L142)
- Explain control-token entry and unchanged cross-photo privacy safeguards.
  [`README.md:1`](../Mentra/README.md#L1)
