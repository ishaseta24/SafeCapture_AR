using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

// Pixelates detected objects before JPEG encoding.
// Boxes lerp smoothly toward new detections each frame and fade out when lost.
// autoMaskAll=true (default): all detections masked automatically.
// autoMaskAll=false: manual click-to-select via ObjectSelectionManager.
[DefaultExecutionOrder(-100)]
public class PrivacyBlur : MonoBehaviour, IDetectionConsumer
{
    [SerializeField] private FrameCapture _frameCapture;
    [SerializeField, Min(1)]  private int   _blockSize     = 16;
    [SerializeField, Min(0)]  private int   _paddingPixels = 8;
    [SerializeField]          private bool  _autoMaskAll   = true;
    [SerializeField, Range(1f, 20f)]  private float _lerpSpeed    = 10f; // how fast boxes follow detections
    [SerializeField, Range(0f, 5f)]   private float _maskHoldSec  = 0.8f; // seconds to hold mask after object disappears
    [SerializeField, Range(0.5f, 5f)] private float _fadeSpeed    = 3f;  // how fast boxes fade after hold expires

    private ObjectSelectionManager _selectionManager;
    private readonly List<int> _selectedTrackIds = new List<int>();
    private byte[] _failClosedRestoreBuffer;

    // Latest raw result from YOLO (written from detection thread, read in Update)
    private VisionResult _latestResult;
    private readonly object _lock = new object();

    // Smoothed boxes in normalized [0-1] space
    private readonly List<SmoothBox> _smoothBoxes = new List<SmoothBox>();

    private class SmoothBox
    {
        public string label;
        public float  nx1, ny1, nx2, ny2; // normalized coords
        public float  alpha;               // 1 = fully visible, 0 = gone
        public bool   matched;
        public float  unmatchedSec;        // time since last matched — holds blur stable
    }

    public void OnDetectionResult(VisionResult result)
    {
        lock (_lock) _latestResult = result;
    }

    private void Awake()
    {
        _selectionManager = GetComponent<ObjectSelectionManager>();
    }

    private void OnEnable()
    {
        if (_frameCapture != null)
            _frameCapture.PreEncodeFilter += PixelateSelected;
    }

    private void OnDisable()
    {
        if (_frameCapture != null)
            _frameCapture.PreEncodeFilter -= PixelateSelected;
    }

    private void Update()
    {
        if (!_autoMaskAll) return;

        VisionResult result;
        lock (_lock) result = _latestResult;

        float dt = Time.deltaTime;
        float lerpT   = 1f - Mathf.Exp(-_lerpSpeed * dt); // framerate-independent lerp
        float fadeIn  = _fadeSpeed * dt;
        float fadeOut = _fadeSpeed * dt;

        // Mark all existing smooth boxes as unmatched
        foreach (var sb in _smoothBoxes) sb.matched = false;

        // Match incoming detections to existing smooth boxes
        if (result?.objects != null && result.width > 0 && result.height > 0)
        {
            foreach (var obj in result.objects)
            {
                if (obj == null || obj.box == null) continue;
                if (obj.falseDetection && (_selectionManager == null
                    || _selectionManager.GetClassMaskOverride(obj.label) != ClassMaskOverride.Mask))
                    continue;

                // Normalize box to [0-1]
                float tnx1 = obj.box.x1 / result.width;
                float tny1 = obj.box.y1 / result.height;
                float tnx2 = obj.box.x2 / result.width;
                float tny2 = obj.box.y2 / result.height;

                float tcx = (tnx1 + tnx2) * 0.5f;
                float tcy = (tny1 + tny2) * 0.5f;

                // Find nearest existing smooth box with same label
                SmoothBox best = null;
                float bestDist = float.MaxValue;
                foreach (var sb in _smoothBoxes)
                {
                    if (sb.matched || sb.label != obj.label) continue;
                    float cx = (sb.nx1 + sb.nx2) * 0.5f;
                    float cy = (sb.ny1 + sb.ny2) * 0.5f;
                    float d  = (cx - tcx) * (cx - tcx) + (cy - tcy) * (cy - tcy);
                    if (d < bestDist) { bestDist = d; best = sb; }
                }

                if (best == null)
                {
                    // New object — snap into place immediately
                    best = new SmoothBox { label = obj.label, nx1 = tnx1, ny1 = tny1, nx2 = tnx2, ny2 = tny2, alpha = 0f };
                    _smoothBoxes.Add(best);
                }

                // Lerp toward target
                best.nx1 = Mathf.Lerp(best.nx1, tnx1, lerpT);
                best.ny1 = Mathf.Lerp(best.ny1, tny1, lerpT);
                best.nx2 = Mathf.Lerp(best.nx2, tnx2, lerpT);
                best.ny2 = Mathf.Lerp(best.ny2, tny2, lerpT);
                best.alpha        = Mathf.Min(1f, best.alpha + fadeIn);
                best.matched      = true;
                best.unmatchedSec = 0f;
            }
        }

        // Hold mask stable for _maskHoldSec after object disappears, then fade out.
        for (int i = _smoothBoxes.Count - 1; i >= 0; i--)
        {
            var sb = _smoothBoxes[i];
            if (!sb.matched)
            {
                sb.unmatchedSec += dt;
                if (sb.unmatchedSec > _maskHoldSec)
                {
                    sb.alpha -= fadeOut;
                    if (sb.alpha <= 0f) _smoothBoxes.RemoveAt(i);
                }
            }
        }
    }

    private void PixelateSelected(Texture2D texture)
    {
        var pixels = texture.GetPixelData<byte>(0);
        int bytesPerPixel = pixels.Length / (texture.width * texture.height);
        if (bytesPerPixel < 3) return;

        if (_autoMaskAll)
        {
            foreach (var sb in _smoothBoxes)
            {
                if (_selectionManager != null
                    && _selectionManager.GetClassMaskOverride(sb.label) == ClassMaskOverride.Unmask)
                    continue;
                PixelateSmoothBox(pixels, texture, bytesPerPixel, sb);
            }
        }
        else
        {
            if (_selectionManager == null) return;

            bool hasSelection = _selectionManager.HasSelection;
            bool failClosed = hasSelection && _selectionManager.ShouldMaskFullFrame;
            byte[] originalPixels = null;
            if (failClosed)
            {
                if (!_selectionManager.HasUnmaskClassOverride)
                {
                    PixelateRegion(pixels, texture.width, texture.height, bytesPerPixel,
                        0, 0, texture.width - 1, texture.height - 1, Mathf.Max(_blockSize, 24));
                    return;
                }

                // Preserve fail-closed masking for the rest of the image, then
                // restore only explicitly unmasked detections below.
                if (_failClosedRestoreBuffer == null
                    || _failClosedRestoreBuffer.Length != pixels.Length)
                    _failClosedRestoreBuffer = new byte[pixels.Length];
                originalPixels = _failClosedRestoreBuffer;
                for (int i = 0; i < pixels.Length; i++)
                    originalPixels[i] = pixels[i];
                PixelateRegion(pixels, texture.width, texture.height, bytesPerPixel,
                    0, 0, texture.width - 1, texture.height - 1, Mathf.Max(_blockSize, 24));
                RestoreUnmaskedClassDetections(pixels, originalPixels, texture, bytesPerPixel);
            }

            // Explicit class masks apply to every matching detection even when the
            // default policy is manual selection.
            PixelateExplicitlyMaskedClasses(pixels, texture, bytesPerPixel);
            if (!hasSelection)
                return;

            _selectionManager.CopySelectedTrackIds(_selectedTrackIds);
            foreach (int trackId in _selectedTrackIds)
            {
                if (_selectionManager.GetSelectedTrackClassMaskOverride(trackId) == ClassMaskOverride.Unmask)
                    continue;

                BoundingBox box; int sourceWidth, sourceHeight;
                if (!_selectionManager.TryGetLastKnownDetection(trackId, out box, out sourceWidth, out sourceHeight)
                    || box == null || sourceWidth <= 0 || sourceHeight <= 0) continue;

                float sx = (float)texture.width / sourceWidth;
                float sy = (float)texture.height / sourceHeight;
                int left = Mathf.Clamp(Mathf.FloorToInt(box.x1 * sx) - _paddingPixels, 0, texture.width - 1);
                int right = Mathf.Clamp(Mathf.CeilToInt(box.x2 * sx) + _paddingPixels, 0, texture.width - 1);
                int top = Mathf.Clamp(Mathf.FloorToInt(box.y1 * sy) - _paddingPixels, 0, texture.height - 1);
                int bottom = Mathf.Clamp(Mathf.CeilToInt(box.y2 * sy) + _paddingPixels, 0, texture.height - 1);
                PixelateRegion(pixels, texture.width, texture.height, bytesPerPixel,
                    left, texture.height - 1 - bottom, right, texture.height - 1 - top, Mathf.Max(1, _blockSize));
            }

            if (originalPixels != null)
            {
                System.Array.Clear(originalPixels, 0, originalPixels.Length);
            }
        }
    }

    private void PixelateSmoothBox(
        NativeArray<byte> pixels, Texture2D texture, int bytesPerPixel, SmoothBox sb)
    {
        if (sb.alpha < 0.1f) return;

        // Expand each side by 7.5% of the box dimension → 15% bigger total.
        float padX = (sb.nx2 - sb.nx1) * 0.075f;
        float padY = (sb.ny2 - sb.ny1) * 0.075f;

        int left   = Mathf.Clamp((int)((sb.nx1 - padX) * texture.width),  0, texture.width  - 1);
        int right  = Mathf.Clamp((int)((sb.nx2 + padX) * texture.width),  0, texture.width  - 1);
        int top    = Mathf.Clamp((int)((sb.ny1 - padY) * texture.height), 0, texture.height - 1);
        int bottom = Mathf.Clamp((int)((sb.ny2 + padY) * texture.height), 0, texture.height - 1);

        int texBottom = texture.height - 1 - bottom;
        int texTop    = texture.height - 1 - top;
        int block     = Mathf.Max(1, Mathf.RoundToInt(_blockSize * sb.alpha));

        PixelateRegion(pixels, texture.width, texture.height, bytesPerPixel,
            left, texBottom, right, texTop, block);
    }

    private void PixelateExplicitlyMaskedClasses(
        NativeArray<byte> pixels, Texture2D texture, int bytesPerPixel)
    {
        VisionResult result;
        lock (_lock) result = _latestResult;
        if (result?.objects == null || result.width <= 0 || result.height <= 0)
            return;

        float scaleX = (float)texture.width / result.width;
        float scaleY = (float)texture.height / result.height;
        foreach (VisionObject obj in result.objects)
        {
            if (obj == null || obj.box == null
                || _selectionManager.GetClassMaskOverride(obj.label) != ClassMaskOverride.Mask)
                continue;

            int left = Mathf.Clamp(Mathf.FloorToInt(obj.box.x1 * scaleX) - _paddingPixels, 0, texture.width - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(obj.box.x2 * scaleX) + _paddingPixels, 0, texture.width - 1);
            int top = Mathf.Clamp(Mathf.FloorToInt(obj.box.y1 * scaleY) - _paddingPixels, 0, texture.height - 1);
            int bottom = Mathf.Clamp(Mathf.CeilToInt(obj.box.y2 * scaleY) + _paddingPixels, 0, texture.height - 1);
            PixelateRegion(pixels, texture.width, texture.height, bytesPerPixel,
                left, texture.height - 1 - bottom, right, texture.height - 1 - top, Mathf.Max(1, _blockSize));
        }
    }

    private void RestoreUnmaskedClassDetections(
        NativeArray<byte> pixels, byte[] originalPixels, Texture2D texture, int bytesPerPixel)
    {
        VisionResult result;
        lock (_lock) result = _latestResult;
        if (result?.objects == null || result.width <= 0 || result.height <= 0)
            return;

        float scaleX = (float)texture.width / result.width;
        float scaleY = (float)texture.height / result.height;
        foreach (VisionObject obj in result.objects)
        {
            if (obj == null || obj.box == null
                || _selectionManager.GetClassMaskOverride(obj.label) != ClassMaskOverride.Unmask)
                continue;

            int left = Mathf.Clamp(Mathf.FloorToInt(obj.box.x1 * scaleX) - _paddingPixels, 0, texture.width - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(obj.box.x2 * scaleX) + _paddingPixels, 0, texture.width - 1);
            int top = Mathf.Clamp(Mathf.FloorToInt(obj.box.y1 * scaleY) - _paddingPixels, 0, texture.height - 1);
            int bottom = Mathf.Clamp(Mathf.CeilToInt(obj.box.y2 * scaleY) + _paddingPixels, 0, texture.height - 1);
            int texBottom = texture.height - 1 - bottom;
            int texTop = texture.height - 1 - top;
            for (int y = texBottom; y <= texTop; y++)
            for (int x = left; x <= right; x++)
            {
                int offset = (y * texture.width + x) * bytesPerPixel;
                for (int channel = 0; channel < bytesPerPixel; channel++)
                    pixels[offset + channel] = originalPixels[offset + channel];
            }
        }
    }

    private static void PixelateRegion(
        NativeArray<byte> pixels, int width, int height, int bytesPerPixel,
        int left, int bottom, int right, int top, int blockSize)
    {
        left   = Mathf.Clamp(left,   0, width  - 1);
        right  = Mathf.Clamp(right,  0, width  - 1);
        bottom = Mathf.Clamp(bottom, 0, height - 1);
        top    = Mathf.Clamp(top,    0, height - 1);
        if (left > right || bottom > top) return;

        for (int blockY = bottom; blockY <= top; blockY += blockSize)
        {
            int blockTop = Mathf.Min(blockY + blockSize - 1, top);
            for (int blockX = left; blockX <= right; blockX += blockSize)
            {
                int blockRight = Mathf.Min(blockX + blockSize - 1, right);
                int r = 0, g = 0, b = 0, count = 0;
                for (int y = blockY; y <= blockTop; y++)
                for (int x = blockX; x <= blockRight; x++)
                {
                    int o = (y * width + x) * bytesPerPixel;
                    r += pixels[o]; g += pixels[o + 1]; b += pixels[o + 2];
                    count++;
                }
                byte ar = (byte)(r / count), ag = (byte)(g / count), ab = (byte)(b / count);
                for (int y = blockY; y <= blockTop; y++)
                for (int x = blockX; x <= blockRight; x++)
                {
                    int o = (y * width + x) * bytesPerPixel;
                    pixels[o] = ar; pixels[o + 1] = ag; pixels[o + 2] = ab;
                }
            }
        }
    }
}
