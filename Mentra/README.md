# SafeCapture for Mentra Live

This folder is the separate Mentra integration. `RayBan/`, the Quest app, and
the SAM relay remain unchanged. Mentra code must not import files from
`RayBan/` at runtime.

**Status:** one physical Wi-Fi photo was delivered and processed successfully.
The recent-photo gallery still needs user confirmation after receiver deployment; USB and serial
capture remain unverified. Uses SDK **3.3.0** for glasses app **3.3.0 / build 303000422**.

## Quick start on a new Mac

Prerequisites: macOS 13+, Xcode Command Line Tools with Swift Package Manager,
Node.js 24+, npm, Python 3, and physical Mentra Live glasses. The native build
was verified with Swift 6.3.3. The repository includes the shared YOLO model at
`Assets/SafeCapture/Models/yolov8n.onnx`; no Ray-Ban runtime dependency is needed.

From the repository root, install the independent receiver dependencies and
create a private configuration without replacing an existing one:

```bash
npm --prefix Mentra/laptop ci
cp -n Mentra/laptop/.env.example Mentra/laptop/.env
openssl rand -hex 32
```

Privately put the generated value into `SAFE_CAPTURE_INGEST_TOKEN` in
`Mentra/laptop/.env`. Keep `SAFE_CAPTURE_AUTO_MASK_CLASSES=tv,laptop` to enable
automatic screen/device masking. Never share or commit the token or `.env`.

Create the Mac app defaults and start the receiver:

```bash
python3 Mentra/macos/configure-env.py
npm --prefix Mentra/laptop start
```

In another terminal, build and open the Mac app:

```bash
bash Mentra/macos/build.sh
```

Open **http://127.0.0.1:8091/** in your browser. In the Mac app, explicitly
connect the glasses, confirm Wi-Fi, and click **Single photo**. Processed
photos populate the in-memory gallery; detected TVs/laptops are masked before
display. Allow Bluetooth/local-network permissions when macOS requests them.
The detailed setup and privacy limitations below still apply.

## Capture path

```text
Mentra Live
  ├─ Bluetooth control ← Mentra/macos native Mac app
  └─ JPEG + requestId over local Wi-Fi, with bearer authentication
       └─ Mentra/laptop: local YOLO → configured automatic + manual object pixelation
             └─ Browser viewer: processed JPEG snapshots + in-memory recent-photo gallery (optional MJPEG)
```

Start with the documented photo-upload API to validate the capture, local
inference, and masking path. These are individual snapshots, not continuous
video or a promised frame rate. A later streaming receiver can feed the same
privacy pipeline.

**No iPhone or iOS app is required.** The native Mac app owns the SDK connection,
device picker, Wi-Fi setup, and photo requests. Bluetooth controls the glasses;
the normal camera-image upload travels over Wi-Fi to the laptop's LAN address.

The implementation plan is in
[`../implementation-artifacts/spec-mentra-local-capture.md`](../implementation-artifacts/spec-mentra-local-capture.md).

## Isolation rules

- Put Mentra-specific source, dependencies, tests, and setup here.
- Copy the reusable laptop pipeline into this folder instead of moving or
  changing the Ray-Ban version.
- Read the existing `Assets/SafeCapture/Models/yolov8n.onnx` model without
  modifying it.
- Use a different default port from Ray-Ban so both services can run together.
- Keep raw frames in memory; do not expose a raw-image download endpoint.
- Never commit tokens, local environment files, camera frames, or dependency
  directories.

## Connect and take a photo

1. Configure `SAFE_CAPTURE_INGEST_TOKEN` in `Mentra/laptop/.env`, then start the
   receiver with `npm --prefix Mentra/laptop start`. Generate a private token
   with `openssl rand -hex 32` if needed; never share it or commit it.
   Set `SAFE_CAPTURE_AUTO_MASK_CLASSES=tv,laptop` there to automatically mask
   detected TVs and laptops on every **Single photo** before its first publish.
   Only `tv` and `laptop` are accepted (either or both, comma-separated);
   an unset or empty value disables automatic masking. Invalid classes reject
   receiver startup. Receiver configuration changes require a restart.
2. Run `python3 Mentra/macos/configure-env.py` once. It privately reuses existing
   receiver settings and creates `Mentra/macos/.env` as a link to the app's
   sandbox-owned configuration. The token is not printed or bundled.
3. Build and open the Mac app with `bash Mentra/macos/build.sh`. Quit an older
   running copy before rebuilding. The app automatically loads `.env` defaults.
4. Disconnect any phone app holding the glasses. Scan in the Mac app, select
   the glasses explicitly, and connect.
5. Camera privacy setup runs automatically once the glasses finish booting.
   The app waits for acknowledgement that gallery mode was disabled.
6. Connect the glasses and Mac to a reachable trusted Wi-Fi network. Already
   connected glasses do not need their Wi-Fi provisioned again.
7. Confirm the prefilled receiver URL and token. The URL must use the Mac's
   current LAN address; update `.env` if the address changes.
8. Click **Single photo**. Connecting or joining Wi-Fi never takes a photo.
   Start serial snapshots only when explicitly desired.

### Private `.env` defaults

Edit `Mentra/macos/.env` and click **Reload .env**, or restart the app.
`.env.example` documents the supported keys: receiver URLs/token, upload mode,
ADB serial, and optional Wi-Fi SSID/password. Simple `KEY=value`, quoted values,
comments, and an optional `export` prefix are supported; shell commands and
variable interpolation are never executed.

The real file is stored with owner-only permissions under
`~/Library/Containers/local.safecapture.mentra/Data/Library/Application Support/SafeCaptureMentra/.env`.
The project link is ignored by Git; builds never embed credentials. This is
**plaintext persistence**, explicitly requested by the user, not a keychain.
Do not share the file. Changing fields or clearing the session token does not
rewrite it. File changes never automatically connect, provision Wi-Fi, confirm
a USB tunnel, or take photos.

The receiver still reads its own `Mentra/laptop/.env`. The setup command copies
its settings once, without overwriting existing app configuration. If its token
changes later, update both files and restart the receiver.

There is **no firmware-version validation step or version admission gate**,
at the user's request. This does not establish firmware compatibility: capture
can still fail. No firmware is installed by this app. If camera setup or an
upload fails, correct the connection/token/receiver issue. **Retry camera setup**
is needed only when camera settings were not confirmed; an upload failure does
not undo a confirmed setting. Serial snapshots stop on errors, and retries are
manual. Error messages expose only fixed categories or numeric HTTP statuses,
never upstream descriptions that might contain credentials.

The photo receiver accepts the SDK 3.3 multipart contract: one JPEG `photo`,
one `requestId`, and optional exact metadata `type=photo_upload` and
`success=true`. It still rejects duplicate/unknown fields, invalid metadata,
oversized bodies, corrupt/non-JPEG files, and unauthenticated requests.

The browser displays complete processed JPEGs, refreshing when the published
revision changes. This avoids browsers buffering a lone MJPEG frame until another
arrives. The current-photo `/snapshot.jpg?frameId=…` and optional `/stream`
endpoints remain available. Neither exposes the unprocessed camera buffer.

### Recent-photo gallery

The viewer follows the latest processed capture by default. Its visible gallery
holds **up to 20 unique source photos and 64 MiB total**, including small
thumbnails. The oldest saved photos are evicted first if either limit is reached,
so large images can reduce the count below 20. Click a thumbnail to select a
**read-only** saved preview. New captures still appear in the gallery but never
steal that selection. **Show latest** resumes following and restores current-photo
manual controls. Evicted/discarded selections show an explicit unavailable message
rather than silently displaying another photo. An old unchanged latest still
remains selectable for manual masking; the stale label alone is not a timeout.

History contains only already-rendered processed JPEGs, thumbnails resized from
those processed JPEGs, and bounded scalar metadata. Thumbnail generation runs
serially with photo processing; a failed thumbnail leaves the safe full image
available with a placeholder. Only the current raw working frame remains in
memory for manual re-rendering. No historical raw buffers, image files,
localStorage, or IndexedDB are used. **History begins with future captures after
deployment; restarting or closing the receiver clears it.** Nothing imports
existing photos or automatically takes new ones. The original **Single photo** /
`capture-photo` defaults, SDK 3.3.0 configuration, and capture behavior are unchanged.

Editing current masks replaces that source photo's saved render without adding a
capture or changing its capture time. The old version is invalidated before
rendering; current preview/thumbnail are hidden during the edit. Failed or withheld
renders cannot serve a previous less-masked version. Older saved snapshots cannot
be re-masked because their raw sources are not retained.

`/status` includes bounded `history` metadata (limits, memory bytes, revision,
and photo metadata), never image bytes. Read-only
`/history/:frameId/photo.jpg?revision=…` and
`/history/:frameId/thumbnail.jpg?revision=…` require exact in-memory source IDs and
monotonic published revisions. They use `Cache-Control: no-store` and return
404 for missing/evicted/withheld photos or missing thumbnails, and 409 for stale
or missing revisions. They never resolve filesystem paths or fall back to raw or
latest images. Viewing photos and metadata requires no control token on this
trusted-LAN prototype.

### Automatic masking and browser controls

Open the receiver viewer to see processed photos; **no browser control token or
box clicks are needed for automatic masks**. The viewer shows active automatic
classes and their masked-object count separately from manual selections. Each new
photo derives its automatic masks only from its own detections, so missing or stale
automatic track IDs from earlier photos do not force later stills to black.

Automatic policy masks **only detected, configured `tv`/`laptop` regions**, not
every screen or sensitive content. A `laptop` mask covers the **entire detected
laptop**, not just its display. Missed detections, other screens, arbitrary text,
and documents may remain visible. This prototype is not universal privacy.
Automatic boxes stay masked; clicking one explains the policy without asking
for a token or toggling it.

For other detected objects, click **Enter control token**, and privately enter the
same `SAFE_CAPTURE_INGEST_TOKEN` configured for the receiver. The token stays in
that tab's memory only; the viewer also prompts on a selection if none was entered.
Click a non-automatic detected box to toggle manual pixelation. A successful
selection updates the effective masked-object count, cyan box, and processed
image. The effective count includes automatic and manual masks without double
counting. Selection does not take a photo. Manual mutations still require the token.
Authentication, missed-object, busy, and changed-photo errors remain in a separate
action message until another deliberate action; periodic status updates do not
erase them.

Selections use the identity of the photo actually loaded, including when token
entry takes time. If another photo arrives, an old click is rejected without
changing masks and the viewer refreshes; click again deliberately after it loads.
Status exposes `frame_id`; `/snapshot.jpg?frameId=…` requires that identity and
returns HTTP 409 if it is missing or changed, preventing mismatched metadata and
pixels. Current-source identity survives mask edits, but changes on a new photo,
explicit clear, or receiver restart; older processed history keeps its own source
identity until eviction. Both `/selection` and `/selection/clear` require the
expected current `frameId` in their authenticated JSON body. Refresh older viewer
tabs after a receiver update.

These still-photo controls do not weaken cross-photo safeguards: lost manually
selected tracks, stale manual tracking gaps, processing failures, and frame-size
changes retain the existing full-frame-mask/output-withholding behavior.
Automatic masks are separate from persistent manual tracks; inference or rendering
errors still fail closed. **Clear manual selections & discard photo** removes
manual selections and the current photo (including its gallery entry), then waits
for a new manual capture. Older processed history remains available, read-only.
It never disables the automatic policy: the next photo is automatically processed
under the same configured classes. Do not clear just to dismiss an error.

## Hardware and SDK assumptions

- Target **Mentra Live**, not display-only MentraOS-compatible glasses.
- This build pins Bluetooth SDK **3.3.0**. Its release manifest names glasses
  app **3.3.0 / build 303000422**. Mentra recommends matching all glasses
  software components; this app skips that verification as requested.
- Native macOS 13+ is supported by the Apple SDK repository. The verified
  public `3.3.0` package declares macOS support, although the macOS documentation
  still includes an older warning about using development sources. Build and
  hardware checks must establish compatibility rather than assuming it.
- The Mac and glasses must be able to reach the Mac's LAN address. `localhost`
  on the glasses does not refer to the Mac.
- The Mac needs Bluetooth permission; local networking permission may also
  be requested. A properly bundled `.app` supplies usage descriptions.
- Camera capture indicators remain enabled.
- Local inference means camera images are not sent to a cloud inference
  provider. It does not mean the upstream SDK makes no network requests:
  Mentra documents default usage analytics and a separate startup update check.

## References

- [Mentra Bluetooth SDK](https://docs.mentraglass.com/bluetooth-sdk/overview)
- [Native macOS setup](https://docs.mentraglass.com/bluetooth-sdk/macos)
- [Apple SDK 3.3.0 source](https://github.com/Mentra-Community/mentra-bluetooth-sdk-ios/tree/3.3.0)
- [Camera upload and streaming](https://docs.mentraglass.com/bluetooth-sdk/camera-streaming)
- [Starter Kit quickstart](https://docs.mentraglass.com/bluetooth-sdk/quickstart)
- [Matching glasses software](https://docs.mentraglass.com/bluetooth-sdk/software-update)
