# SafeCapture

**Real-time privacy protection for XR cameras — built on Meta Quest 3.**

SafeCapture detects sensitive objects (screens, documents, faces) through the Quest 3 passthrough camera and pixelates them before the frame is encoded or streamed — protecting bystanders automatically, in real time, on device.

> Built at XR Hack 2026 by Isha Seta, Ananth Narasimhan, and Anu Srinivasan.

---

## Idea & Impact

As camera-equipped AR glasses spread — Meta Quest 3, Ray-Ban Meta, and what comes next — they introduce a new class of privacy risk: anyone wearing them can inadvertently capture and broadcast sensitive content. A meeting room screen, a colleague's document, a stranger's face in a coffee shop. Today there is no automatic protection. The person wearing the glasses has to remember to look away.

SafeCapture adds an automatic privacy layer directly on the headset. The camera feed never leaves the device unfiltered. Sensitive objects are detected using a local YOLO model, optionally verified by Meta SAM 3.1, and pixelated **before JPEG encoding** — so the stream that reaches a browser or recording is already clean.

The impact is broad:

- **Ethical egocentric data collection** — robot training data and lifelogging pipelines can be built without capturing private content
- **Enterprise AR** — employees can use headsets in sensitive environments without IT worrying about screen capture
- **Research** — HCI and accessibility researchers can record studies without manual redaction passes
- **Everyday use** — bystanders are protected by default, without needing to consent or even be aware

---

## What it does

- **Live MJPEG stream** — Quest 3 passthrough camera streams to any browser over Wi-Fi; no app install needed on the viewer side
- **On-device YOLO detection** — YOLOv8n runs locally on Quest via Unity Sentis (ONNX); no internet required for detection
- **SAM verification** — optional Meta SAM 3.1 relay call cross-checks YOLO detections for higher confidence
- **Automatic privacy blur** — all detections are pixelated by default; blur holds for 0.8 s after a detection is lost so single missed frames never cause a blink
- **Click-to-select masking** — click any bounding box in the browser to toggle that object's blur individually
- **Fail-closed mode** — if a selected object disappears, the full frame is pixelated until it is reacquired
- **Voice commands** — say "blur all screens" or "unmask people" to set per-class masking rules hands-free
- **Smooth rendering** — boxes lerp toward each new detection; blur regions are 15% larger than the box to absorb jitter

---

## Platforms & Details

| Layer | Technology |
|---|---|
| Headset | Meta Quest 3 |
| Engine | Unity 6000.0.55 + Meta XR SDK |
| On-device inference | Unity Sentis — YOLOv8n ONNX (80 COCO classes) |
| Cloud verification | Meta SAM 3.1 (open-vocabulary, via Node.js relay) |
| Voice | Meta Voice SDK (Wit.ai) — `VoiceCommandRecognizer` + `VoiceCommandParser` |
| Relay server | Node.js 18 + Express + sharp.js |
| Streaming protocol | MJPEG over HTTP — works in any browser with no plugin |
| Privacy encoding | Pre-encode pixelation on `NativeArray<byte>` texture data before JPEG |

---

## Architecture

```
Quest 3 — Unity App
  ├─ FrameCapture            WebCamTexture → byte[] → PreEncodeFilter pipeline
  │
  ├─ VisionScheduler         timed detection loop, time-budgeted to avoid dropped frames
  │    ├─ YoloSentisProvider  YOLOv8n ONNX inference on-device (Unity Sentis)
  │    ├─ SamRelayProvider    POST /detect → relay (optional SAM verification)
  │    └─ VisionObjectTracker assigns stable track IDs per detection instance
  │
  ├─ VoiceCommandRecognizer  Meta Voice SDK mic listener
  │    └─ VoiceCommandParser  maps utterances to per-class mask/unmask rules
  │
  ├─ ObjectSelectionManager  browser click state + fail-closed track logic
  ├─ PrivacyBlur             smooth lerp boxes, 0.8 s hold, 15% padding, pixelate before encode
  ├─ BoxOverlay              smooth lerp boxes drawn on frame before encode
  └─ MjpegServer             HTTP server — MJPEG stream + /select click endpoint

Relay — Node.js (runs on laptop, optional)
  ├─ server.js    Express entry — POST /detect, POST /voice, GET /health
  ├─ sam.js       Meta SAM 3.1 API client + <|box;...|> token parser
  ├─ voice.js     voice command relay endpoint
  ├─ auth.js      RELAY_TOKEN middleware
  ├─ limits.js    1-in-flight cap + daily API call limit
  ├─ log.js       JSONL call logger
  └─ annotate.js  SVG debug frame annotator (DEBUG_FRAMES=true)

Browser
  └─ http://<quest-ip>:8080/   MJPEG stream — click boxes to toggle blur, voice rules apply live
```

---

## Milestones

| Milestone | Status | Description |
|---|---|---|
| M1 | ✅ | MJPEG live stream from Quest 3 passthrough camera to browser |
| M2 | ✅ | Meta SAM 3.1 integration — open-vocabulary detection via relay |
| M3 | ✅ | Local YOLOv8n ONNX inference on-device via Unity Sentis |
| M4 | ✅ | Smooth auto-masking, time-budgeted inference, click-to-select browser UI |
| M5 | ✅ | Voice commands — "blur all screens", per-class mask rules, relay voice endpoint |
| M6 | 🛠️ | Performance pass — smooth box rendering, blur hold, pixel-precise relay masking |

---

## Future Implementation Goals

- **3D object persistence** — track detected objects in AR world space across frames so blur follows the object even when it temporarily leaves the YOLO detection window
- **Pixel-precise masking** — relay-side Otsu threshold masking (`mask.js`) already prototyped; promotes from box-level to contour-level privacy blur
- **Face detection** — dedicated face model for GDPR-grade face blurring without relying on COCO "person" class
- **Ray-Ban Meta support** — extend the pipeline to camera glasses (ManageXR MDM integration); pitch becomes "privacy layer for the AI glasses people already wear"
- **On-device anonymized export** — record redacted video with per-frame redaction provenance metadata for research datasets
- **WebRTC streaming** — replace MJPEG with WebRTC for sub-100 ms latency and encrypted peer-to-peer delivery
- **Consent UI** — opt-in overlay so bystanders can register preferences that the headset respects automatically

---

## Running locally

### Prerequisites

- Meta Quest 3 with developer mode enabled
- Unity 6000.0.55 with Meta XR SDK and Unity Sentis
- Node.js 18+
- ADB connected to Quest over USB or Wi-Fi

### 1. Configure the relay (optional — needed for SAM verification and voice relay)

```bash
cd relay
cp .env.example .env
# edit .env — set MODEL_API_KEY and RELAY_TOKEN
npm install
npm start
```

### 2. Configure the Quest app

Create `config.json`:

```json
{
  "relayUrl": "http://<your-laptop-ip>:3000",
  "relayToken": "<your-relay-token>",
  "detectIntervalSec": 30,
  "maxImageWidth": 960,
  "prompts": ["laptop screen", "phone screen"]
}
```

Push to the Quest:

```bash
adb push config.json /sdcard/Android/data/com.Ohlone.Quest/files/config.json
```

### 3. Build and run

- Build the Unity project for Android (Quest 3 target)
- Install and launch on Quest
- Tap **Connect** → note the IP → tap **Start Streaming**
- Open `http://<quest-ip>:8080/` in any browser

YOLO boxes appear after the first local inference. Click a box to toggle its blur. Say "blur all screens" to apply voice rules. SAM verification requires the relay running above.

---

## Project structure

```
Assets/SafeCapture/Scripts/
  Config/     AppConfig.cs               loads config.json from device or StreamingAssets
  Vision/     VisionScheduler.cs         timed, time-budgeted detection loop
              YoloSentisProvider.cs      on-device YOLOv8n ONNX via Unity Sentis
              SamRelayProvider.cs        HTTP client for relay /detect
              VisionObjectTracker.cs     stable per-instance track IDs
              VisionResult.cs            result + bounding box structs
              VoiceCommandParser.cs      maps voice utterances to class mask rules
              IVisionProvider.cs         provider interface
  Consumers/  PrivacyBlur.cs             smooth lerp boxes, blur hold, pixelate before encode
              BoxOverlay.cs              smooth lerp boxes drawn on frame
              ObjectSelectionManager.cs  browser selection state + fail-closed logic
              IDetectionConsumer.cs      consumer interface
  FrameCapture.cs                        WebCamTexture → PreEncodeFilter → JPEG pipeline
  MjpegServer.cs                         MJPEG stream server + /select endpoint
  VoiceCommandRecognizer.cs              Meta Voice SDK mic listener
  StreamManager.cs                       server lifecycle
  SafeCaptureUIController.cs             UI + status panel

relay/src/
  server.js    Express entry — /detect, /voice, /health
  sam.js       Meta SAM 3.1 client + box token parser
  voice.js     voice command endpoint
  auth.js      token middleware
  limits.js    in-flight cap + daily limit
  log.js       JSONL call logger
  annotate.js  debug frame annotator
```

---

## License

MIT License — Copyright (c) 2026 Isha Seta, Ananth Narasimhan, Anu Srinivasan. See [LICENSE](LICENSE).

---

## Secrets — never commit

| File | Why |
|---|---|
| `relay/.env` | MODEL_API_KEY and RELAY_TOKEN |
| `Assets/StreamingAssets/config.json` | relayToken for Quest |
| `Assets/XR/Settings/AuthBuildSettings.asset` | NSDK access token |
| `relay/debug/` | camera frames |
| `relay/logs/` | call logs |
