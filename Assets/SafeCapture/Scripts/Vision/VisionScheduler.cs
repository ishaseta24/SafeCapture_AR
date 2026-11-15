using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// Two-tier detection pipeline:
//   YOLO  every 500ms  — local, bounding boxes; Otsu masks computed on-device in PrivacyBlur
//   SAM   every 60s    — cloud, discovery only (objects YOLO missed)
public class VisionScheduler : MonoBehaviour
{
    [SerializeField] private StreamManager      _streamManager;
    [SerializeField] private FrameCapture       _frameCapture;
    [SerializeField] private YoloSentisProvider _yoloProvider;
    [SerializeField] private float              _yoloIntervalSec      = 0.5f;
    [SerializeField] private float              _maskIntervalSec      = 2f;
    [SerializeField] private float              _samVerifyIntervalSec = 60f;

    // ── Public stats ──────────────────────────────────────────────────────────

    public int   YoloCallCount       { get; private set; }
    public int   YoloLastObjectCount { get; private set; }
    public float YoloLastLatencyMs   { get; private set; }

    public int   SamCallCount       { get; private set; }
    public int   SamLastObjectCount { get; private set; }
    public float SamNextAllowedIn   { get; private set; }

    public int   MaskCallCount     { get; private set; }
    public float MaskNextAllowedIn { get; private set; }

    public bool          YoloPaused    { get; private set; }
    public bool          SamPaused     { get; private set; }
    public VisionResult  LatestResult  => _latestYoloResult;

    public float YoloIntervalSec    => _yoloIntervalSec;
    public float SamVerifyIntervalSec => _samVerifyIntervalSec;

    public bool EnsureDetectionLabel(string label)
    {
        return _yoloProvider != null && _yoloProvider.AddRuntimeTargetLabel(label);
    }

    public void SetYoloInterval(float seconds)
    {
        _yoloIntervalSec = Mathf.Max(0.1f, seconds);
    }

    public void SetSamInterval(float seconds)
    {
        _samVerifyIntervalSec = Mathf.Max(1f, seconds);
        // Reset next verify time so change takes effect immediately
        _nextSamVerifyTime = Time.time + _samVerifyIntervalSec;
    }

    public void ToggleYolo()
    {
        YoloPaused = !YoloPaused;
        if (YoloPaused)
        {
            // Clear stream overlay immediately
            _latestYoloResult = null;
            _latestFrame      = null;
            BroadcastResult(new VisionResult { objects = new VisionObject[0] });
        }
    }

    public void ToggleSam()
    {
        SamPaused = !SamPaused;
        if (SamPaused && _latestYoloResult?.objects != null)
        {
            // Reset verification — show all as unverified yellow, nothing hidden
            foreach (var o in _latestYoloResult.objects)
            {
                o.verified       = false;
                o.falseDetection = false;
            }
            BroadcastResult(_latestYoloResult);
        }
    }

    public string ProviderName { get; private set; } = "—";
    public string StatusLog    { get; private set; } = "";

    // backwards compat
    public int   CallCount       => YoloCallCount + SamCallCount;
    public int   LastObjectCount => YoloLastObjectCount;
    public float NextCallIn      => SamNextAllowedIn;

    // ── Private state ─────────────────────────────────────────────────────────

    private SamRelayProvider  _samProvider;
    private MaskRelayProvider _maskProvider;
    private readonly VisionObjectTracker _objectTracker = new VisionObjectTracker();

    private bool  _yoloInFlight;
    private float _nextYoloTime;

    private bool  _maskInFlight;
    private float _nextMaskTime;

    private bool  _samInFlight;
    private float _nextSamVerifyTime;

    private VisionResult _latestYoloResult;
    private byte[]       _latestFrame;
    private int          _latestFrameW;
    private int          _latestFrameH;

    private readonly Queue<string>  _logQueue  = new Queue<string>();
    private readonly StringBuilder  _logSb     = new StringBuilder();
    private const int               MaxLogLines = 6;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        _samProvider  = new SamRelayProvider();
        _maskProvider = new MaskRelayProvider();
        ProviderName = _yoloProvider != null ? "YOLO (local)" : "SAM (cloud)";
        Debug.Log($"[VisionScheduler] Starting — {ProviderName}");
    }

    private void Update()
    {
        SamNextAllowedIn  = Mathf.Max(0f, _nextSamVerifyTime - Time.time);
        MaskNextAllowedIn = Mathf.Max(0f, _nextMaskTime - Time.time);

        bool isStreaming = _streamManager != null
            && _streamManager.CurrentState == StreamManager.StreamState.Streaming;

        if (!isStreaming)
        {
            if (_latestYoloResult != null)
            {
                _latestYoloResult = null;
                _latestFrame      = null;
                BroadcastResult(new VisionResult { objects = new VisionObject[0] });
            }
            return;
        }

        // YOLO — local inference every 500ms
        if (!YoloPaused && !_yoloInFlight && Time.time >= _nextYoloTime
            && _frameCapture != null && _frameCapture.LatestFrame != null)
        {
            _nextYoloTime = Time.time + Mathf.Max(0.1f, _yoloIntervalSec);
            _ = DispatchYolo(_frameCapture.LatestFrame,
                             _frameCapture.CaptureWidth,
                             _frameCapture.CaptureHeight);
        }

        // SAM — cloud discovery every 60s
        if (!SamPaused && !_samInFlight && Time.time >= _nextSamVerifyTime
            && _latestYoloResult?.objects?.Length > 0 && _latestFrame != null)
            _ = VerifyWithSam();
    }

    // ── YOLO dispatch ─────────────────────────────────────────────────────────

    private async Task DispatchYolo(byte[] frame, int w, int h)
    {
        _yoloInFlight  = true;
        _latestFrame   = frame;
        _latestFrameW  = w;
        _latestFrameH  = h;
        try
        {
            VisionResult result = null;

            if (_yoloProvider != null)
            {
                try
                {
                    result = await _yoloProvider.DetectAsync(frame, w, h);
                    if (result != null)
                    {
                        ProviderName         = "YOLO (local)";
                        YoloCallCount++;
                        YoloLastObjectCount  = result.objects?.Length ?? 0;
                        YoloLastLatencyMs    = result.latency_ms;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VisionScheduler] YOLO error: {e.Message}");
                    result = null;
                }
            }

            if (result == null)
            {
                var selectionManager = GetComponent<ObjectSelectionManager>();
                if (selectionManager != null)
                    selectionManager.NotifyInferenceUnavailable();
                return;
            }

            _objectTracker.Update(result);
            _latestYoloResult = result;

            string labels = result.objects?.Length > 0
                ? string.Join(", ", result.objects.Select(o => o.label))
                : "none";
            AddLog($"=> yolo - {labels}");

            BroadcastResult(result);
        }
        finally { _yoloInFlight = false; }
    }

    // ── Mask dispatch ─────────────────────────────────────────────────────────

    private async Task DispatchMasks()
    {
        _maskInFlight = true;
        _nextMaskTime = Time.time + _maskIntervalSec;
        try
        {
            var current = _latestYoloResult;
            if (current?.objects == null || current.objects.Length == 0) return;

            var maskResult = await _maskProvider.GetMasksAsync(
                _latestFrame, current.objects, _latestFrameW, _latestFrameH);

            if (maskResult?.objects == null) return;

            MaskCallCount++;

            // Apply masks to the current result by label — YOLO may have refreshed during the await.
            var live = _latestYoloResult;
            if (live?.objects == null) return;

            foreach (var masked in maskResult.objects)
            {
                if (masked.mask == null) continue;
                foreach (var obj in live.objects)
                {
                    if (!string.Equals(obj.label, masked.label, StringComparison.OrdinalIgnoreCase)) continue;
                    obj.mask  = masked.mask;
                    obj.maskW = masked.maskW;
                    obj.maskH = masked.maskH;
                    break;
                }
            }

            AddLog($"=> mask - {maskResult.objects.Count(o => o.mask != null)} masks @ {maskResult.latency_ms:F0}ms");
            BroadcastResult(live);
        }
        catch (Exception e) { Debug.LogWarning($"[VisionScheduler] mask error: {e.Message}"); }
        finally { _maskInFlight = false; }
    }

    // ── SAM discovery ─────────────────────────────────────────────────────────

    private async Task VerifyWithSam()
    {
        _samInFlight      = true;
        _nextSamVerifyTime = Time.time + _samVerifyIntervalSec;
        try
        {
            var yoloResult = _latestYoloResult;

            var samResult = await _samProvider.DetectAsync(
                _latestFrame, _latestFrameW, _latestFrameH);

            if (samResult == null) return;

            SamCallCount++;
            SamLastObjectCount = samResult.objects?.Length ?? 0;

            if (yoloResult?.objects != null)
            {
                var yoloLabelSet = new HashSet<string>(
                    yoloResult.objects.Select(o => o.label),
                    StringComparer.OrdinalIgnoreCase);

                // Result = Union(YOLO, SAM).
                // YOLO objects found by SAM too → verified (green).
                // YOLO objects SAM didn't find   → unverified (yellow), still shown.
                foreach (var yObj in yoloResult.objects)
                {
                    yObj.verified       = samResult.objects != null &&
                        samResult.objects.Any(s => string.Equals(
                            s.label, yObj.label, StringComparison.OrdinalIgnoreCase));
                    yObj.falseDetection = false;
                }

                // SAM-only objects (valid box, label not already from YOLO).
                var samOnly = samResult.objects?
                    .Where(s => !yoloLabelSet.Contains(s.label)
                             && s.box.x2 > s.box.x1 && s.box.y2 > s.box.y1)
                    .Select(s => new VisionObject
                    {
                        label          = s.label,
                        box            = s.box,
                        confidence     = 1f,
                        samOnly        = true,
                        falseDetection = false,
                    })
                    .ToArray();

                if (samOnly?.Length > 0)
                    yoloResult.objects = yoloResult.objects.Concat(samOnly).ToArray();

                var allLabels = yoloResult.objects.Select(o => o.label);
                AddLog($"=> union: {string.Join(", ", allLabels)}");

                BroadcastResult(yoloResult);
            }
        }
        catch (Exception e) { Debug.LogWarning($"[VisionScheduler] SAM verify error: {e.Message}"); }
        finally { _samInFlight = false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void BroadcastResult(VisionResult result)
    {
        foreach (var consumer in GetComponents<IDetectionConsumer>())
            consumer.OnDetectionResult(result);
    }

    private void AddLog(string entry)
    {
        _logQueue.Enqueue(entry);
        if (_logQueue.Count > MaxLogLines) _logQueue.Dequeue();
        _logSb.Clear();
        foreach (var line in _logQueue) _logSb.AppendLine(line);
        StatusLog = _logSb.ToString();
    }

}
