using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using UnityEngine;

// Local YOLOv8n inference via Unity Sentis (com.unity.ai.inference) — no network required.
// Uses ScheduleIterable with a per-frame time budget so the main thread never blocks long
// enough to freeze the video stream. PrivacyBlur lerp keeps masks smooth between detections.
// Export model with: yolo export model=yolov8n.pt format=onnx simplify=True opset=12
// Drop yolov8n.onnx into Assets/SafeCapture/Models/ and assign in Inspector.
public class YoloSentisProvider : MonoBehaviour, IVisionProvider
{
    [SerializeField] private ModelAsset  _modelAsset;
    [SerializeField] private BackendType _backend             = BackendType.CPU;
    [SerializeField] private float       _confidenceThreshold = 0.25f;
    [SerializeField] private float       _iouThreshold        = 0.45f;
    [SerializeField] private int         _maxDetections       = 20;
    [Tooltip("Only detect these labels. Leave empty to detect all 80 COCO classes.")]
    [SerializeField] private string[]    _targetLabels        = { "hand", "laptop", "tv", "cell phone", "cup", "bottle", "water bottle", "book", "remote", "apple" };

    private const int    InputSize      = 640;
    private const double FrameBudgetSec = 0.010; // max 10ms of inference work per frame

    private readonly HashSet<string> _runtimeTargetLabels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private Worker    _worker;
    private Texture2D _inputTex;
    private float[]   _tensorData; // allocated once in Awake

    public bool AddRuntimeTargetLabel(string label)
    {
        string canonical;
        if (!CocoLabels.TryNormalizeVoiceName(label, out canonical))
            return false;

        _runtimeTargetLabels.Add(canonical);
        return true;
    }

    private void Awake()
    {
        if (_modelAsset == null)
        {
            Debug.LogError("[YoloSentisProvider] No model asset assigned.");
            return;
        }
        var model   = ModelLoader.Load(_modelAsset);
        _worker     = new Worker(model, _backend);
        _inputTex   = new Texture2D(InputSize, InputSize, TextureFormat.RGB24, false);
        _tensorData = new float[3 * InputSize * InputSize];
    }

    private void OnDestroy() => _worker?.Dispose();

    // Kicks off a coroutine that runs inference in time-budgeted slices.
    // Each frame we spend at most FrameBudgetSec on inference then yield,
    // so the video stream is never blocked long enough to go blank.
    public Task<VisionResult> DetectAsync(byte[] jpegBytes, int originalWidth, int originalHeight)
    {
        if (_worker == null) return Task.FromResult<VisionResult>(null);
        var tcs = new TaskCompletionSource<VisionResult>();
        StartCoroutine(RunInference(jpegBytes, originalWidth, originalHeight, tcs));
        return tcs.Task;
    }

    private IEnumerator RunInference(byte[] jpegBytes, int origW, int origH,
                                     TaskCompletionSource<VisionResult> tcs)
    {
        float t0 = Time.realtimeSinceStartup;

        // 1. Decode JPEG → Texture2D (main thread)
        var raw = new Texture2D(2, 2);
        ImageConversion.LoadImage(raw, jpegBytes);

        // 2. Resize to 640×640 via GPU blit (main thread)
        var rt = RenderTexture.GetTemporary(InputSize, InputSize, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(raw, rt);
        RenderTexture.active = rt;
        _inputTex.ReadPixels(new Rect(0, 0, InputSize, InputSize), 0, 0);
        _inputTex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        Destroy(raw);

        // 3. Pixels → float[CHW], flip Y (Unity bottom-up → YOLO top-down)
        var pixels = _inputTex.GetPixels32();
        for (int y = 0; y < InputSize; y++)
        {
            int srcRow = (InputSize - 1 - y) * InputSize;
            int dstRow = y * InputSize;
            for (int x = 0; x < InputSize; x++)
            {
                var p = pixels[srcRow + x];
                _tensorData[dstRow + x]                             = p.r / 255f;
                _tensorData[InputSize * InputSize + dstRow + x]     = p.g / 255f;
                _tensorData[2 * InputSize * InputSize + dstRow + x] = p.b / 255f;
            }
        }

        // 4. Schedule inference — iterate with per-frame time budget
        // Many fast layers run per frame; we only yield when the budget is spent.
        var inputTensor = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize), _tensorData);
        var enumerator  = _worker.ScheduleIterable(inputTensor);

        double frameStart = Time.realtimeSinceStartupAsDouble;
        while (enumerator.MoveNext())
        {
            if (Time.realtimeSinceStartupAsDouble - frameStart >= FrameBudgetSec)
            {
                yield return null; // hand control back — stream keeps running
                frameStart = Time.realtimeSinceStartupAsDouble;
            }
        }

        // 5. Readback + post-process (back on main thread after all layers done)
        if (_worker.PeekOutput() is not Tensor<float> outputTensor)
        {
            inputTensor.Dispose();
            tcs.SetResult(null);
            yield break;
        }

        var readable = outputTensor.ReadbackAndClone();
        inputTensor.Dispose();

        var objects = PostProcess(readable, origW, origH);
        readable.Dispose();

        float latencyMs = (Time.realtimeSinceStartup - t0) * 1000f;
        tcs.SetResult(new VisionResult
        {
            frame_id   = Guid.NewGuid().ToString("N")[..8],
            width      = origW,
            height     = origH,
            latency_ms = latencyMs,
            objects    = objects
        });
    }

    // ── Post-processing ───────────────────────────────────────────────────────

    private VisionObject[] PostProcess(Tensor<float> output, int origW, int origH)
    {
        int numAnchors = output.shape[2];
        var candidates = new List<(BoundingBox box, float score, int cls)>();

        for (int i = 0; i < numAnchors; i++)
        {
            float maxScore = 0f;
            int   maxCls   = 0;
            for (int c = 4; c < 84; c++)
            {
                float s = output[0, c, i];
                if (s > maxScore) { maxScore = s; maxCls = c - 4; }
            }

            if (maxScore < _confidenceThreshold) continue;

            string detectedLabel = CocoLabels.Get(maxCls);
            if (_targetLabels != null && _targetLabels.Length > 0
                && !_runtimeTargetLabels.Contains(detectedLabel))
            {
                string detected = detectedLabel.Trim().ToLowerInvariant();
                bool inList = false;
                foreach (var t in _targetLabels)
                {
                    string target = (t ?? "").Trim().ToLowerInvariant();
                    if (target.Length == 0) continue;
                    if (detected.Contains(target) || target.Contains(detected)
                        || Levenshtein(detected, target) <= 2)
                    { inList = true; break; }
                }
                if (!inList) continue;
            }

            float scaleX = (float)origW / InputSize;
            float scaleY = (float)origH / InputSize;
            float cx = output[0, 0, i], cy = output[0, 1, i];
            float bw = output[0, 2, i], bh = output[0, 3, i];

            candidates.Add((new BoundingBox
            {
                x1 = (cx - bw * 0.5f) * scaleX,
                y1 = (cy - bh * 0.5f) * scaleY,
                x2 = (cx + bw * 0.5f) * scaleX,
                y2 = (cy + bh * 0.5f) * scaleY
            }, maxScore, maxCls));
        }

        var kept   = ApplyNMS(candidates);
        var result = new VisionObject[kept.Count];
        for (int i = 0; i < kept.Count; i++)
        {
            var (box, score, cls) = kept[i];
            result[i] = new VisionObject
            {
                label      = CocoLabels.Get(cls),
                id         = cls,
                class_id   = cls,
                confidence = score,
                box        = box
            };
        }
        return result;
    }

    private List<(BoundingBox, float, int)> ApplyNMS(List<(BoundingBox box, float score, int cls)> dets)
    {
        dets.Sort((a, b) => b.score.CompareTo(a.score));
        var kept       = new List<(BoundingBox, float, int)>();
        var suppressed = new bool[dets.Count];

        for (int i = 0; i < dets.Count && kept.Count < _maxDetections; i++)
        {
            if (suppressed[i]) continue;
            kept.Add(dets[i]);
            for (int j = i + 1; j < dets.Count; j++)
                if (!suppressed[j] && IoU(dets[i].box, dets[j].box) > _iouThreshold)
                    suppressed[j] = true;
        }
        return kept;
    }

    private static int Levenshtein(string a, string b)
    {
        int m = a.Length, n = b.Length;
        var d = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 0; j <= n; j++) d[0, j] = j;
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                d[i, j] = a[i-1] == b[j-1]
                    ? d[i-1, j-1]
                    : 1 + Mathf.Min(d[i-1, j], Mathf.Min(d[i, j-1], d[i-1, j-1]));
        return d[m, n];
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
