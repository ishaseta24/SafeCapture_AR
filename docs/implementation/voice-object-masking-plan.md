# Voice object masking

## Goal

Let a Quest user issue explicit voice commands such as “mask bottles” and “unmask bottles.” Apply each command to every current and future detection of that COCO class, before the frame is JPEG-encoded.

## Approach

1. Capture short audio only while the user has started a voice command.
2. Send mono PCM WAV audio to the existing authenticated relay. The relay uses Meta Muse Voice Transcribe (`muse-voice-transcribe-1.0`, `PUSH_TO_TALK`) with the existing server-side Meta API key.
3. Parse only the final transcript. Accept only `mask <class>` and `unmask <class>` for supported COCO labels; reject unsupported or ambiguous text.
4. Add the canonical class to YOLO's runtime detection targets without changing the Inspector-configured targets.
5. Store a runtime class-level override (`inherit`, `mask`, or `unmask`). Apply it in `PrivacyBlur` before encoding. An explicit unmask overrides automatic masking and per-instance selection for that label.
6. Show recording, transcription, result, and failure status in the Quest UI.

## Constraints

- Keep the Meta API key on the relay; Quest authenticates to the relay using the existing token pattern.
- Make clear that audio is sent to Meta. Do not persist audio or transcripts or run speech recognition when recording is inactive.
- Permission, network, rate-limit, or transcription errors must leave mask state unchanged and show a recoverable error.
- Explicit class overrides take precedence over SAM false-detection filtering, so a voice-requested class does not need a new SAM prompt or API call.
- Meta Muse Voice Transcribe is cloud-based and costs $0.18 per processed audio hour.
- Do not integrate Wispr Flow or send audio directly from Quest to Meta.

## Main files

- `Assets/SafeCapture/Scripts/Vision/VoiceCommandParser.cs` — strict command parsing and label normalization.
- `Assets/SafeCapture/Scripts/Vision/YoloSentisProvider.cs` and `Assets/SafeCapture/Scripts/Vision/VisionScheduler.cs` — include voice-requested labels in runtime detection filtering.
- `Assets/SafeCapture/Scripts/VoiceCommandRecognizer.cs` — microphone capture and relay request.
- `Assets/SafeCapture/Scripts/Consumers/ObjectSelectionManager.cs` — class-level runtime overrides.
- `Assets/SafeCapture/Scripts/Consumers/PrivacyBlur.cs` — apply class overrides before JPEG encoding.
- `Assets/SafeCapture/Scripts/SafeCaptureUIController.cs` and `Assets/SafeCapture/Scenes/SafeCapture.unity` — record control and status.
- `relay/src/server.js` and `relay/src/voice.js` — authenticated transcription route; validate WAV format and bounds, and keep audio/transcript out of logs and persistent storage.

## Manual verification

- On Quest 3, confirm microphone permission and explicit start/stop behavior.
- Say “mask bottles”; verify all bottle detections are pixelated, including later detections.
- Say “mask chairs” for a class not in the Inspector target list; verify YOLO begins returning chairs and they are pixelated.
- Confirm “mask chairs” still pixelates the class if SAM verification has no matching prompt/result.
- Say “unmask bottles”; verify bottles are unmasked while other classes retain their prior behavior.
- Try unsupported speech and permission/network/API failures; confirm no mask changes and a visible error.
- Confirm inactive capture sends no audio and the Quest never receives the Meta API key.
