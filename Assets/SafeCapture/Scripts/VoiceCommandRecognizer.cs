using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Explicit push-to-talk capture. Audio and transcript exist only in memory and
/// are sent only to the authenticated relay; commands are applied after final ASR.
/// </summary>
public sealed class VoiceCommandRecognizer : MonoBehaviour
{
    private const int RecordingSeconds = 8;
    private const int TargetSampleRate = 24000;
    private const int MaxUploadBytes = 1024 * 1024;

    [SerializeField] private ObjectSelectionManager _selectionManager;
    [SerializeField] private VisionScheduler _visionScheduler;

    private AudioClip _recording;
    private bool _permissionPending;
    private bool _transcribing;
    private float _recordingStartedAt;
    private string _status = "Voice commands off. Audio is sent to Meta when you record.";

    public string Status => _status;
    public bool IsRecording { get; private set; }
    public bool IsBusy => IsRecording || _permissionPending || _transcribing;
    public event Action<string> StatusChanged;

    private void Update()
    {
        if (IsRecording && _recording != null
            && (Microphone.GetPosition(null) >= _recording.samples
                || Time.realtimeSinceStartup - _recordingStartedAt >= RecordingSeconds))
            StopRecordingAndTranscribe();
    }

    public void ToggleRecording()
    {
        if (IsRecording)
            StopRecordingAndTranscribe();
        else
            StartRecording();
    }

    public void StartRecording()
    {
        if (IsBusy)
            return;

        if (!AppConfig.Instance.IsConfigured)
        {
            SetStatus("Voice commands unavailable: relay is not configured.");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            _permissionPending = true;
            SetStatus("Microphone permission is required.");
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                if (!_permissionPending || !isActiveAndEnabled)
                    return;
                _permissionPending = false;
                BeginCapture();
            };
            callbacks.PermissionDenied += _ =>
            {
                if (!_permissionPending)
                    return;
                _permissionPending = false;
                SetStatus("Microphone permission denied. Allow it in app settings to retry.");
            };
            callbacks.PermissionDeniedAndDontAskAgain += _ =>
            {
                if (!_permissionPending)
                    return;
                _permissionPending = false;
                SetStatus("Microphone permission blocked. Allow it in app settings to retry.");
            };
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            return;
        }
#endif
        BeginCapture();
    }

    public void StopRecordingAndTranscribe()
    {
        if (!IsRecording || _recording == null)
            return;

        int capturedSamples = Mathf.Clamp(Microphone.GetPosition(null), 0, _recording.samples);
        Microphone.End(null);
        IsRecording = false;

        if (capturedSamples < Mathf.CeilToInt(_recording.frequency * 0.2f))
        {
            ReleaseRecording();
            SetStatus("No command captured. Tap Record to try again.");
            return;
        }

        float[] interleavedSamples = null;
        float[] monoSamples = null;
        try
        {
            int channels = Mathf.Max(1, _recording.channels);
            interleavedSamples = new float[capturedSamples * channels];
            if (!_recording.GetData(interleavedSamples, 0))
            {
                SetStatus("Could not read microphone audio. Please try again.");
                return;
            }

            monoSamples = DownmixToMono(interleavedSamples, channels);
            byte[] wav = EncodeMonoPcmWav(monoSamples, _recording.frequency, TargetSampleRate);
            if (wav.Length > MaxUploadBytes)
            {
                Array.Clear(wav, 0, wav.Length);
                SetStatus("Recording is too large. Try a shorter command.");
                return;
            }

            SetStatus("Transcribing with Meta through the authenticated relay...");
            _transcribing = true;
            _ = SendForTranscription(wav);
        }
        catch (Exception)
        {
            SetStatus("Could not prepare microphone audio. Please try again.");
        }
        finally
        {
            if (interleavedSamples != null)
                Array.Clear(interleavedSamples, 0, interleavedSamples.Length);
            if (monoSamples != null)
                Array.Clear(monoSamples, 0, monoSamples.Length);
            ReleaseRecording();
        }
    }

    private void BeginCapture()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            SetStatus("No microphone is available on this device.");
            return;
        }

        _recording = Microphone.Start(null, false, RecordingSeconds, TargetSampleRate);
        if (_recording == null)
        {
            SetStatus("Could not start the microphone. Please try again.");
            return;
        }

        IsRecording = true;
        _recordingStartedAt = Time.realtimeSinceStartup;
        SetStatus("Recording command… tap Stop when finished. Audio will be sent to Meta.");
    }

    private async Task SendForTranscription(byte[] wav)
    {
        UnityWebRequest request = null;
        try
        {
            var cfg = AppConfig.Instance;
            request = new UnityWebRequest(cfg.relayUrl + "/voice/transcribe", "POST");
            request.uploadHandler = new UploadHandlerRaw(wav);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/octet-stream");
            request.SetRequestHeader("X-Relay-Token", cfg.relayToken);
            request.timeout = 40;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401)
                    SetStatus("Voice command rejected by relay authentication. Check relay configuration.");
                else if (request.responseCode == 413 || request.responseCode == 400)
                    SetStatus("Recording was not accepted. Try a shorter command.");
                else if (request.responseCode == 429)
                    SetStatus("Transcription is busy or rate limited. Please retry shortly.");
                else if (request.responseCode == 422)
                    SetStatus("No clear command was recognized. Say “mask <object>” or “unmask <object>”.");
                else
                    SetStatus("Transcription unavailable due to a network or service error. Please retry.");
                return;
            }

            VoiceTranscriptionResponse response =
                JsonUtility.FromJson<VoiceTranscriptionResponse>(request.downloadHandler.text);
            if (response == null || !response.final)
            {
                SetStatus("Transcription did not return a final result. No command was applied.");
                return;
            }

            VoiceMaskCommand command;
            string error;
            if (!VoiceCommandParser.TryParse(response.transcript, out command, out error))
            {
                SetStatus(error);
                return;
            }

            if (_selectionManager == null)
            {
                SetStatus("Voice command could not be applied: masking is unavailable.");
                return;
            }

            if (_visionScheduler == null || !_visionScheduler.EnsureDetectionLabel(command.Label))
            {
                SetStatus("Object detection is unavailable. No mask rule was applied.");
                return;
            }

            ClassMaskOverride value = command.Type == VoiceMaskCommandType.Mask
                ? ClassMaskOverride.Mask
                : ClassMaskOverride.Unmask;
            if (!_selectionManager.TrySetClassMaskOverride(command.Label, value))
            {
                SetStatus("Unsupported object class. No mask state was changed.");
                return;
            }

            SetStatus($"{(value == ClassMaskOverride.Mask ? "Masked" : "Unmasked")} {command.Label}.");
        }
        catch (Exception)
        {
            SetStatus("Transcription failed due to a network or service error. Please retry.");
        }
        finally
        {
            _transcribing = false;
            Array.Clear(wav, 0, wav.Length);
            request?.Dispose();
        }
    }

    private void ReleaseRecording()
    {
        if (_recording != null)
        {
            Destroy(_recording);
            _recording = null;
        }
    }

    private void OnDisable()
    {
        _permissionPending = false;
        if (IsRecording)
        {
            Microphone.End(null);
            IsRecording = false;
            ReleaseRecording();
            SetStatus("Recording cancelled.");
        }
    }

    private void OnDestroy()
    {
        if (IsRecording)
            Microphone.End(null);
        IsRecording = false;
        ReleaseRecording();
    }

    private void SetStatus(string value)
    {
        _status = value;
        StatusChanged?.Invoke(value);
    }

    private static byte[] EncodeMonoPcmWav(float[] input, int inputRate, int outputRate)
    {
        if (input == null || input.Length == 0 || inputRate <= 0 || outputRate <= 0)
            throw new ArgumentException("Audio samples and sample rates are required.");

        int outputSamples = Mathf.Max(1, Mathf.CeilToInt(input.Length * (float)outputRate / inputRate));
        byte[] wav = new byte[44 + outputSamples * 2];
        int dataBytes = outputSamples * 2;
        WriteAscii(wav, 0, "RIFF");
        WriteInt32(wav, 4, 36 + dataBytes);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        WriteInt32(wav, 16, 16);
        WriteInt16(wav, 20, 1);
        WriteInt16(wav, 22, 1);
        WriteInt32(wav, 24, outputRate);
        WriteInt32(wav, 28, outputRate * 2);
        WriteInt16(wav, 32, 2);
        WriteInt16(wav, 34, 16);
        WriteAscii(wav, 36, "data");
        WriteInt32(wav, 40, dataBytes);

        for (int i = 0; i < outputSamples; i++)
        {
            float sourcePosition = i * (float)inputRate / outputRate;
            int lower = Mathf.Min(Mathf.FloorToInt(sourcePosition), input.Length - 1);
            int upper = Mathf.Min(lower + 1, input.Length - 1);
            float fraction = sourcePosition - lower;
            float sample = Mathf.Clamp(Mathf.Lerp(input[lower], input[upper], fraction), -1f, 1f);
            short pcm = (short)Mathf.RoundToInt(sample < 0f ? sample * 32768f : sample * 32767f);
            WriteInt16(wav, 44 + i * 2, pcm);
        }
        return wav;
    }

    private static float[] DownmixToMono(float[] interleaved, int channels)
    {
        if (channels <= 1)
            return (float[])interleaved.Clone();
        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            float sum = 0f;
            for (int channel = 0; channel < channels; channel++)
                sum += interleaved[frame * channels + channel];
            mono[frame] = sum / channels;
        }
        return mono;
    }

    private static void WriteAscii(byte[] bytes, int offset, string value)
    {
        for (int i = 0; i < value.Length; i++)
            bytes[offset + i] = (byte)value[i];
    }

    private static void WriteInt16(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)((value >> 8) & 0xff);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)((value >> 8) & 0xff);
        bytes[offset + 2] = (byte)((value >> 16) & 0xff);
        bytes[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    [Serializable]
    private sealed class VoiceTranscriptionResponse
    {
        public string transcript;
        public bool final;
    }
}
