# SafeCapture

Privacy-safe live streaming and AI-powered object detection from Meta Quest 3.

SafeCapture streams the Quest 3 passthrough camera to any browser while running open-vocabulary detection via Meta SAM 3.1 — detecting and bounding private objects (screens, documents, faces) in real time so they can be blurred or flagged before the stream is shared. Designed for ethical egocentric data collection, robot training pipelines, and responsible camera-glasses deployments.

---

## What it does

- **Live MJPEG stream** — Quest 3 passthrough camera → any browser over Wi-Fi, no app install needed
- **AI object detection** — Quest sends frames to a local relay server, which calls Meta SAM 3.1 to detect objects by text prompt (e.g. "laptop screen", "phone screen")
- **Bounding box overlay** — detected objects are drawn on the stream frame before encoding, visible in the browser viewer
- **Privacy-first architecture** — frames never leave your local network; the relay runs on your laptop

---

## Hackathon tracks

**Main: Virtuous Reality**
SafeCapture is technology for good. It protects bystanders and private information as camera glasses spread, and makes egocentric data collection (robot training data, lifelogging) ethical. Judges reward real-world impact and responsibility — that is the whole pitch.

**Backup: AI Horizons**
Lean on the AI pipeline: open-vocabulary detection (YOLOE + SAM 3.1), voice rules through Muse, and 3D object persistence across frames.

**Sponsor add-on: ManageXR (AI and smart glasses)**
Enter only if the Ray-Ban stream runs through the pipeline. The pitch becomes "privacy layer for the AI glasses people already wear", covering both Work and Life examples. Without glasses support it is a weak fit.

---

## Architecture

```
Quest 3 (Unity app)
  └─ FrameCapture        captures passthrough camera via WebCamTexture
  └─ MjpegServer         streams JPEG frames to browser over HTTP
  └─ VisionScheduler     sends frames to relay every N seconds
       └─ SamRelayProvider  POST /detect → relay server

Relay (Node.js, runs on laptop)
  └─ POST /detect        receives base64 frame + prompts
  └─ sam.js              calls Meta SAM 3.1 API, parses <|box;...|> tokens
  └─ annotate.js         draws SVG overlay for debug frames (DEBUG_FRAMES=true)
  └─ log.js              JSONL log of every call (latency, object count)
  └─ GET /health         liveness check

Browser
  └─ http://<quest-ip>:8080/   MJPEG stream with bounding boxes drawn on frames
```

---

## Milestones

| Milestone | Status | Description |
|---|---|---|
| M1 | ✅ | MJPEG live stream from Quest 3 passthrough camera to browser |
| M2 | ✅ | Meta SAM 3.1 integration — bounding boxes on detected objects |
| M3 | 🔜 | PrivacyBlur — blur detected regions before streaming |
| M4 | 🔜 | Local YOLO model — on-device detection, no cloud API required |
| M5 | 🔜 | Voice rules via Muse API — "blur all screens", "flag faces" |
| M6 | 🔜 | 3D object persistence — track objects across frames in AR space |

---

## Running locally

### Prerequisites

- Meta Quest 3 with developer mode enabled
- Unity 2022.3+ with Meta XR SDK
- Node.js 18+
- ADB connected to Quest over USB or Wi-Fi

### 1. Configure the relay

```bash
cd relay
cp .env.example .env
# edit .env — add MODEL_API_KEY and RELAY_TOKEN
npm install
npm start
```

### 2. Configure the Quest app

Create `config.json` with your relay URL and token:

```json
{
  "relayUrl": "http://<your-laptop-ip>:3000",
  "relayToken": "<your-relay-token>",
  "detectIntervalSec": 30,
  "maxImageWidth": 960,
  "prompts": ["laptop screen", "phone screen"]
}
```

Push it to the Quest:

```bash
adb push config.json /sdcard/Android/data/com.Ohlone.Quest/files/config.json
```

### 3. Build and run

- Build the Unity project for Android (Quest 3 target)
- Install and launch on the Quest
- Tap **Connect** → note the IP shown → tap **Start Streaming**
- Open `http://<quest-ip>:8080/` in a browser on your laptop

Bounding boxes appear in the browser stream within the first SAM call (~30s by default, set `detectIntervalSec: 3` for production).

---

## Project structure

```
Assets/SafeCapture/Scripts/
  Config/         AppConfig.cs           loads config.json from device or StreamingAssets
  Vision/         VisionScheduler.cs     timed detection loop
                  SamRelayProvider.cs    HTTP client for /detect
                  VisionResult.cs        result + bounding box structs
                  IVisionProvider.cs     provider interface
  Consumers/      BoxOverlay.cs          draws boxes on stream frame
                  PrivacyBlur.cs         (stub — M3)
                  IDetectionConsumer.cs  consumer interface
  FrameCapture.cs                        WebCamTexture → MJPEG pipeline
  StreamManager.cs                       server lifecycle
  SafeCaptureUIController.cs             UI + SAM call counter

relay/
  src/
    server.js     Express entry point — POST /detect, GET /health
    sam.js        Meta SAM 3.1 API client + box parser
    annotate.js   debug frame annotator (sharp + SVG)
    log.js        JSONL call logger
    auth.js       relay token middleware
    limits.js     1-in-flight cap + daily call limit
  .env.example    config template
  step0-test.js   standalone SAM API test script
```

---

## Secrets — never commit

| File | Why |
|---|---|
| `relay/.env` | MODEL_API_KEY and RELAY_TOKEN |
| `Assets/StreamingAssets/config.json` | relayToken for Quest |
| `Assets/XR/Settings/AuthBuildSettings.asset` | NSDK access token |
| `relay/debug/` | camera frames |
| `relay/logs/` | call logs |
