# SafeCapture

**A programmable privacy layer for camera glasses.**

Describe in plain English what your camera should never capture. Test it. Then wear it.

> Built at XR Hack 2026 by Isha Seta, Ananth Narasimhan, and Anu Srinivasan.

---

## Inspiration

Camera glasses went mainstream faster than the rules around them. Meta sold more than seven million smart glasses in 2025, and IDC forecasts display-less glasses will reach 13.6 million units in 2026, growing to 27.3 million by 2030.

The people in front of those cameras never agreed to be filmed — and they know it:

- **72%** of Americans worry about being secretly recorded by camera-equipped wearables in public; only **20%** are confident they could recognize smart glasses
- Only **4%** of owners say they always tell nearby people when they are recording
- Two Harvard students built **I-XRAY** on Ray-Ban Meta glasses — automatically going from face, to name, to phone number, to home address

The world is reacting with bans:

- MSC Cruises and Royal Caribbean restrict smart glasses in public areas, restrooms, youth areas, medical areas, and casinos
- Philadelphia's First Judicial District banned all smart glasses with recording capabilities from courthouses (effective 30 March 2026)
- In the UK, Soho House clubs, ATG Theatres, and JD Wetherspoon have banned filming with smart glasses
- One health system's security chief said they prohibit unapproved smart glasses in their facilities

Privacy is also what holds buyers back: **56%** of people unlikely to buy smart glasses name privacy concerns as a reason.

Banning the device throws away everything good about it — hands-free help for a nurse, a technician, a blind user. Warning lights don't work either, since only one in five people can recognize the glasses. We wanted the camera itself to respect people, so the glasses can stay on.

---

## The Insight: Everyone Wants Different Rules

Everyone we showed this to wanted something different:

- A **vlogger** livestreaming their day wants bystanders and licence plates gone
- A **doctor** wants documents, X-rays and children's faces gone
- A **streamer** wants a drink brand they dislike covered up
- A **business owner** wants every face and screen blurred, except staff who opt in

The answer isn't one more fixed filter. It's a **programmable privacy layer**: one engine, with every rule written in plain English.

> *"Blur faces, but let people who show two peace signs unblur, so they can consent to be photographed."*

That sentence becomes a live profile on the glasses in about a second.

---

## What It Does

SafeCapture sits between the camera and everything after it — your screen, your recording, your livestream.

### Private by default
- Every face is blurred the moment it appears
- **Fail-closed**: lost objects stay blurred, and the whole frame blurs during fast camera motion

### Consent by gesture
- ✌️✌️ and you're seen; 👎👎 and you disappear again
- Consent lives only in memory for the session and is wiped on exit — only consenting people's face fingerprints are kept

### Rules anyone can write
- **Wearers** talk to their glasses: *"also blur screens"*, *"make it stronger"*, *"go back to default"* — a friendly voice confirms each change
- **Business owners** type a policy into the dashboard: *"Blur every face and every screen; staff and visitors show a thumbs-up to be seen"* — they see exactly what changed, field by field
- **Developers and AI agents** use the same five MCP tools: `list_capabilities`, `save_profile`, `test_profile`, `activate_profile`, `get_profile`

### Test before you trust
- One click (or one MCP call) runs a profile against synthetic moving-camera footage: slow pans, whip pans, zooms, and a 50-face crowd — no real person's video needed
- Results come back in plain English: *"Tested on 3 moving-camera clips (660 frames): nobody was wrongly unblurred, and a face was briefly not fully covered in 1.8% of frames, mostly during fast camera motion"*

### Any camera
Runs live on **Mentra Live** glasses. Also takes webcams, USB cameras, and network streams (RTMP, SRT, RTSP). We prototyped the same pipeline on **Meta Quest 3**. One profile follows you across all of them.

---

## Real Scenarios

- **Hospitals** — a nurse wears AI glasses for hands-free procedure guidance; patient faces, charts and monitors are removed before anything is saved
- **Field work** — a technician's site-walk footage becomes an inspection report without customers' screens, whiteboards, or employees in it
- **Public life** — a creator streams a city walk, and every passer-by is blurred unless they flash ✌️✌️ to be in the shot
- **Families and schools** — a parent records a school event while other children's faces stay protected
- **Venues** — a club, theatre, or cruise line could require a SafeCapture policy instead of a ban

---

## Platforms

| Platform | Status | Notes |
|---|---|---|
| **Mentra Live** (glasses) | ✅ Live | USB camera feed, real-time blur at 30 fps, voice in/out |
| **Meta Quest 3** | ✅ Prototyped | Passthrough camera, MJPEG stream, YOLO + SAM + voice |
| Webcam / USB camera | ✅ | Any V4L2 or DirectShow device |
| Network stream | ✅ | RTMP, SRT, RTSP |
| Ray-Ban Meta | 🔜 | Planned |

---

## How We Built It

```
Claude / any MCP client · web dashboard · voice on the glasses · typing
                            │
                 MCP tools (one interface)
                            │
   profiles/*.yaml  (validated, tested, hot-reloaded in ~1 s)
                            │
 detect → track → consent memory → fail-closed blur
                            │
 in: Mentra Live (USB), webcam, any stream    out: screen, recording
```

| Component | Technology |
|---|---|
| Object detection | YOLOv8 — people and screens |
| Face detection | InsightFace |
| Gesture detection | MediaPipe |
| Tracking & fail-closed | Hold-and-track logic, full-frame blur during fast motion |
| Rules engine | YAML profiles, validated and hot-reloaded in ~1 s |
| MCP interface | 5 tools: `list_capabilities`, `save_profile`, `test_profile`, `activate_profile`, `get_profile` |
| Voice (glasses) | Glasses mic → local Whisper → Claude + MCP → live profile switch |
| Dashboard | Profiles, plain-English rule editor, test results, start/stop, recording |
| Evaluation | Synthetic moving-camera clips scored for missed faces and false unblurs |

### Quest 3 prototype specifics

| Layer | Technology |
|---|---|
| Engine | Unity 6000.0.55 + Meta XR SDK |
| On-device inference | Unity Sentis — YOLOv8n ONNX (80 COCO classes) |
| Cloud verification | Meta SAM 3.1 open-vocabulary detection |
| Voice ASR | Meta Muse `muse-voice-transcribe-1.0` via relay |
| Voice NLP | On-device parser — 7 mask verbs, 7 unmask verbs, greedy COCO label match |
| Streaming | MJPEG over HTTP |
| Privacy encoding | Pre-encode pixelation on `NativeArray<byte>` before JPEG |

---

## Quest 3 Architecture

```
Quest 3 — Unity App
  ├─ FrameCapture            PassthroughCameraAccess → PreEncodeFilter pipeline
  ├─ VisionScheduler         time-budgeted YOLO loop + SAM verification
  │    ├─ YoloSentisProvider  YOLOv8n ONNX on-device (Unity Sentis)
  │    ├─ SamRelayProvider    POST /detect → relay (Meta SAM 3.1)
  │    └─ VisionObjectTracker stable track IDs per detection instance
  ├─ VoiceCommandRecognizer  push-to-talk → 24 kHz mono PCM WAV → relay
  │    └─ VoiceCommandParser  on-device NLP → class mask rule → ObjectSelectionManager
  ├─ PrivacyBlur             smooth lerp, 0.8 s hold, 15% padding, pixelate before encode
  ├─ BoxOverlay              smooth lerp boxes drawn on frame
  └─ MjpegServer             MJPEG stream + /select click endpoint

Relay — Node.js (runs on laptop)
  ├─ server.js    POST /detect, POST /voice/transcribe, GET /health
  ├─ sam.js       Meta SAM 3.1 client
  ├─ voice.js     Meta Muse ASR endpoint
  ├─ auth.js      RELAY_TOKEN middleware
  └─ limits.js    in-flight cap + daily API limit
```

---

## Detection Capabilities

### YOLO (on-device) — 80 COCO classes

| Category | Objects |
|---|---|
| People | person |
| Vehicles | bicycle, car, motorcycle, airplane, bus, train, truck, boat |
| Electronics | tv, laptop, mouse, remote, keyboard, cell phone, microwave, oven, toaster |
| Animals | bird, cat, dog, horse, sheep, cow, elephant, bear, zebra, giraffe |
| Furniture | bench, chair, couch, potted plant, bed, dining table, toilet, sink, refrigerator |
| Food | banana, apple, sandwich, orange, broccoli, carrot, hot dog, pizza, donut, cake, bottle, wine glass, cup, fork, knife, spoon, bowl |
| Accessories | backpack, umbrella, handbag, tie, suitcase |
| Sports | frisbee, skis, snowboard, sports ball, kite, baseball bat, baseball glove, skateboard, surfboard, tennis racket |
| Other | book, clock, vase, scissors, teddy bear, hair drier, toothbrush |

### Meta SAM 3.1 — open vocabulary

Detects anything you describe: `"face"`, `"whiteboard"`, `"document"`, `"ID card"`, `"credit card"`. Configured via `prompts` in `config.json`. Used for verification — if SAM confirms a YOLO box → green; if not → marked false detection and hidden.

---

## Voice Commands (Quest 3)

Push-to-talk — no always-on microphone. Audio is zeroed from memory immediately after sending.

| Intent | Trigger words | Example |
|---|---|---|
| Mask a class | mask · hide · block · cover · blur · censor · protect | *"hide the laptop"* |
| Unmask a class | unmask · show · unhide · unblock · uncover · reveal · display | *"show person"* |
| Current object | "this" / "that" / "it" | *"blur this"* |

---

## Challenges

- **Motion** — head-mounted cameras whip around constantly; single-frame detection leaks faces, so we built hold-and-track logic and full-frame blur during fast pans
- **Consent without surveillance** — remembering who said yes, without building a face database; consent lives only in RAM for people who opted in and is wiped on exit
- **Honesty** — when a rule asks for something the engine can't do yet, we report it instead of pretending
- **Hardware** — camera crashes and flicker on Quest, and getting a glasses feed into the pipeline reliably

---

## Accomplishments

- **Test before you trust** — any rule runs on synthetic footage in seconds, with zero people wrongly unblurred in every test run, explained in plain English
- **Anyone can write a privacy rule** — a spoken sentence, a typed sentence, or one click becomes a live tested rule on the glasses in about a second
- **Glasses to screen in ~1 second** at a steady 30 fps
- **Crowd-proof** — a 50-face synthetic crowd went from leaking on every frame to leaking on 2–3 frames
- **One engine, four ways in** — Claude, any MCP client, the dashboard, and voice — all through the same five tools

---

## What's Next

- **New detectors as new rules** — licence plates, documents, X-rays, brand logos, anyone can write them
- **Fleet-wide policies** — an organisation pushes one profile to every pair of glasses it manages
- **More devices** — Ray-Ban Meta, headsets, webcams, and livestream servers, all with the same profile
- **3D object persistence** — track objects in AR world space across frames
- **Pixel-precise masking** — relay-side Otsu masking already prototyped; promotes from box-level to contour-level blur
- **WebRTC streaming** — replace MJPEG with WebRTC for sub-100 ms latency

---

## Running Locally

### Mentra Live

```bash
# Install dependencies
pip install -r requirements.txt   # or see mentra/README.md

# Start the pipeline
python main.py --camera mentra
```

### Meta Quest 3

**Prerequisites:** Quest 3 (developer mode), Unity 6000.0.55 + Meta XR SDK + Unity Sentis, Node.js 18+, ADB

```bash
# 1. Start the relay (needed for SAM + voice)
cd relay
cp .env.example .env        # add MODEL_API_KEY and RELAY_TOKEN
npm install && npm start

# 2. Push config to Quest
adb push config.json /sdcard/Android/data/com.Ohlone.Quest/files/config.json

# 3. Build Unity project → Android → install → launch on Quest
#    Tap Connect → Start Streaming → open http://<quest-ip>:8080/
```

`config.json` format:
```json
{
  "relayUrl": "http://<laptop-ip>:3000",
  "relayToken": "<your-relay-token>",
  "maxImageWidth": 960,
  "prompts": ["laptop screen", "phone screen", "face"]
}
```

---

## License

MIT License — Copyright (c) 2026 Isha Seta, Ananth Narasimhan, Anu Srinivasan. See [LICENSE](LICENSE).

---

## Sources

- [Meta sold seven million smart glasses — Heise](https://www.heise.de)
- [Smart glasses forecast 2026–2030 — IDC](https://www.idc.com)
- [Security.org: 2026 Smart Glasses Market Report](https://www.security.org)
- [ConsumerAffairs: survey on secret recording](https://www.consumeraffairs.com)
- [I-XRAY facial recognition on Meta glasses — 404 Media](https://404media.co)
- [Royal Caribbean smart glasses ban](https://www.royalcaribbeanblog.com)
- [Philadelphia courthouse ban](https://www.courts.phila.gov)
- [UK venues ban Meta glasses — The Guardian via Freevacy](https://freevacy.com)
- [Smart glasses and healthcare privacy — Becker's](https://www.beckershospitalreview.com)
