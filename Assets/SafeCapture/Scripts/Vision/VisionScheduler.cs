using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// YOLO runs every _yoloIntervalSec.
// SAM runs every 5s to verify YOLO detections (IoU-based).
// Verified → green box.  False detection → hidden.
public class VisionScheduler : MonoBehaviour
{
    [SerializeField] private FrameCapture       _frameCapture;
    [SerializeField] private YoloSentisProvider _yoloProvider;
    [SerializeField] private float              _yoloIntervalSec    = 1f;
    [SerializeField] private float              _samVerifyIntervalSec = 5f;
    [SerializeField] private float              _verifyIouThreshold = 0.3f;

    // ── Public stats ──────────────────────────────────────────────────────────

    public int   YoloCallCount       { get; private set; }
    public int   YoloLastObjectCount { get; private set; }
    public float YoloLastLatencyMs   { get; private set; }

    public int   SamCallCount       { get; private set; }
    public int   SamLastObjectCount { get; private set; }
    public float SamNextAllowedIn   { get; private set; }

    public string ProviderName { get; private set; } = "—";
    public string StatusLog    { get; private set; } = "";

    // backwards compat
    public int   CallCount       => YoloCallCount + SamCallCount;
    public int   LastObjectCount => YoloLastObjectCount;
    public float NextCallIn      => SamNextAllowedIn;

    // ── Private state ─────────────────────────────────────────────────────────

    private SamRelayProvider _samProvider;

    private bool  _yoloInFlight;
    private float _nextYoloTime;

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
        _samProvider = new SamRelayProvider();
        ProviderName = _yoloProvider != null ? "YOLO (local)" : "SAM (cloud)";
        Debug.Log($"[VisionScheduler] Starting — {ProviderName}");
    }

    private void Update()
    {
        SamNextAllowedIn = Mathf.Max(0f, _nextSamVerifyTime - Time.time);

        // YOLO dispatch
        if (!_yoloInFlight && Time.time >= _nextYoloTime
            && _frameCapture != null && _frameCapture.LatestFrame != null)
        {
            _nextYoloTime = Time.time + Mathf.Max(0.1f, _yoloIntervalSec);
            _ = DispatchYolo(_frameCapture.LatestFrame,
                             _frameCapture.CaptureWidth,
                             _frameCapture.CaptureHeight);
        }

        // SAM verification (runs even while YOLO is in flight)
        if (!_samInFlight && Time.time >= _nextSamVerifyTime
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

            if (result == null) return;

            _latestYoloResult = result;

            string labels = result.objects?.Length > 0
                ? string.Join(", ", result.objects.Select(o => o.label))
                : "none";
            AddLog($"=> yolo - {labels}");

            BroadcastResult(result);
        }
        finally { _yoloInFlight = false; }
    }

    // ── SAM verification ──────────────────────────────────────────────────────

    private async Task VerifyWithSam()
    {
        _samInFlight      = true;
        _nextSamVerifyTime = Time.time + _samVerifyIntervalSec;
        try
        {
            var yoloResult = _latestYoloResult;
            var samResult  = await _samProvider.DetectAsync(_latestFrame, _latestFrameW, _latestFrameH);

            if (samResult == null) return;

            SamCallCount++;
            SamLastObjectCount = samResult.objects?.Length ?? 0;

            var falseLabels = new List<string>();

            if (yoloResult?.objects != null)
            {
                foreach (var yObj in yoloResult.objects)
                {
                    bool found = false;
                    if (samResult.objects != null)
                        foreach (var sObj in samResult.objects)
                            if (IoU(yObj.box, sObj.box) > _verifyIouThreshold) { found = true; break; }

                    yObj.verified       = found;
                    yObj.falseDetection = !found;
                    if (!found) falseLabels.Add(yObj.label);
                }

                string metaLog = falseLabels.Count > 0
                    ? $"=> meta - false detection: {string.Join(", ", falseLabels)}"
                    : "=> meta - all confirmed";
                AddLog(metaLog);

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

    private static float IoU(BoundingBox a, BoundingBox b)
    {
        float ix1   = Mathf.Max(a.x1, b.x1), iy1 = Mathf.Max(a.y1, b.y1);
        float ix2   = Mathf.Min(a.x2, b.x2), iy2 = Mathf.Min(a.y2, b.y2);
        float inter = Mathf.Max(0, ix2 - ix1) * Mathf.Max(0, iy2 - iy1);
        float areaA = (a.x2 - a.x1) * (a.y2 - a.y1);
        float areaB = (b.x2 - b.x1) * (b.y2 - b.y1);
        return inter / (areaA + areaB - inter + 1e-6f);
    }
}
