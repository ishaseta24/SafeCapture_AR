using System.Collections.Generic;
using UnityEngine;

// Draws bounding boxes + labels onto the stream frame before JPEG encoding.
// Boxes lerp smoothly toward new detections and hold for _holdSec after the object is last seen.
// Cyan = user selected   Green = YOLO+SAM   Yellow = YOLO only   Blue = SAM only
[DefaultExecutionOrder(100)]
public class BoxOverlay : MonoBehaviour, IDetectionConsumer
{
    [SerializeField] private FrameCapture _frameCapture;
    [SerializeField] private int          _labelScale = 2;
    [SerializeField, Range(1f, 20f)] private float _lerpSpeed = 10f;
    [SerializeField, Range(0f, 2f)]  private float _holdSec   = 0.8f;

    private static readonly Color Verified   = Color.green;
    private static readonly Color Unverified = Color.yellow;
    private static readonly Color SamOnly    = Color.blue;
    private static readonly Color BgColor    = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextColor  = Color.white;

    private VisionResult _latestResult;
    private ObjectSelectionManager _selectionManager;
    private readonly object _lock = new object();

    private class SmoothBox
    {
        public int    track_id;
        public string label;
        public float  nx1, ny1, nx2, ny2;
        public bool   matched;
        public float  unmatchedSec;
        public bool   verified;
        public bool   samOnly;
    }

    private readonly List<SmoothBox> _smoothBoxes = new List<SmoothBox>();

    private void Awake() => _selectionManager = GetComponent<ObjectSelectionManager>();

    private void OnEnable()
    {
        if (_frameCapture != null) _frameCapture.PreEncodeFilter += Draw;
    }

    private void OnDisable()
    {
        if (_frameCapture != null) _frameCapture.PreEncodeFilter -= Draw;
    }

    public void OnDetectionResult(VisionResult result)
    {
        lock (_lock) _latestResult = result;
    }

    private void Update()
    {
        VisionResult result;
        lock (_lock) result = _latestResult;

        float dt    = Time.deltaTime;
        float lerpT = 1f - Mathf.Exp(-_lerpSpeed * dt);

        foreach (var sb in _smoothBoxes) sb.matched = false;

        if (result?.objects != null && result.width > 0 && result.height > 0)
        {
            foreach (var obj in result.objects)
            {
                if (obj == null || obj.box == null) continue;
                bool selected = _selectionManager != null && _selectionManager.IsSelected(obj.track_id);
                if (obj.falseDetection && !selected) continue;

                float tnx1 = obj.box.x1 / result.width;
                float tny1 = obj.box.y1 / result.height;
                float tnx2 = obj.box.x2 / result.width;
                float tny2 = obj.box.y2 / result.height;
                float tcx  = (tnx1 + tnx2) * 0.5f;
                float tcy  = (tny1 + tny2) * 0.5f;

                SmoothBox best = null;
                float bestDist = float.MaxValue;
                foreach (var sb in _smoothBoxes)
                {
                    if (sb.matched || sb.track_id != obj.track_id) continue;
                    float cx = (sb.nx1 + sb.nx2) * 0.5f;
                    float cy = (sb.ny1 + sb.ny2) * 0.5f;
                    float d  = (cx - tcx) * (cx - tcx) + (cy - tcy) * (cy - tcy);
                    if (d < bestDist) { bestDist = d; best = sb; }
                }

                if (best == null)
                {
                    best = new SmoothBox
                    {
                        track_id = obj.track_id, label = obj.label,
                        nx1 = tnx1, ny1 = tny1, nx2 = tnx2, ny2 = tny2,
                    };
                    _smoothBoxes.Add(best);
                }

                best.nx1 = Mathf.Lerp(best.nx1, tnx1, lerpT);
                best.ny1 = Mathf.Lerp(best.ny1, tny1, lerpT);
                best.nx2 = Mathf.Lerp(best.nx2, tnx2, lerpT);
                best.ny2 = Mathf.Lerp(best.ny2, tny2, lerpT);
                best.matched      = true;
                best.unmatchedSec = 0f;
                best.verified     = obj.verified;
                best.samOnly      = obj.samOnly;
            }
        }

        for (int i = _smoothBoxes.Count - 1; i >= 0; i--)
        {
            var sb = _smoothBoxes[i];
            if (!sb.matched)
            {
                sb.unmatchedSec += dt;
                if (sb.unmatchedSec > _holdSec)
                    _smoothBoxes.RemoveAt(i);
            }
        }
    }

    private void Draw(Texture2D tex)
    {
        if (_smoothBoxes.Count == 0) return;

        foreach (var sb in _smoothBoxes)
        {
            bool selected = _selectionManager != null && _selectionManager.IsSelected(sb.track_id);

            Color  boxColor;
            string tag;
            if (selected)
            {
                boxColor = Color.cyan;
                tag      = "SELECTED " + sb.label;
            }
            else if (sb.samOnly)
            {
                boxColor = SamOnly;
                tag      = sb.label + " (meta)";
            }
            else if (sb.verified)
            {
                boxColor = Verified;
                tag      = sb.label;
            }
            else
            {
                boxColor = Unverified;
                tag      = sb.label + "?";
            }

            int x1  = Mathf.Clamp((int)(sb.nx1 * tex.width),                    0, tex.width  - 1);
            int x2  = Mathf.Clamp((int)(sb.nx2 * tex.width),                    0, tex.width  - 1);
            int ty1 = Mathf.Clamp(tex.height - 1 - (int)(sb.ny2 * tex.height),  0, tex.height - 1);
            int ty2 = Mathf.Clamp(tex.height - 1 - (int)(sb.ny1 * tex.height),  0, tex.height - 1);

            DrawRect(tex, x1, x2, ty1, ty2, boxColor);
            PixelFont.DrawStringBg(tex, tag, x1, ty2 + 1, TextColor, BgColor, _labelScale);
        }

        tex.Apply(false);
    }

    private static void DrawRect(Texture2D tex, int x1, int x2, int ty1, int ty2, Color color)
    {
        for (int x = x1; x <= x2; x++) { tex.SetPixel(x, ty1, color); tex.SetPixel(x, ty2, color); }
        for (int y = ty1; y <= ty2; y++) { tex.SetPixel(x1, y, color); tex.SetPixel(x2, y, color); }
    }
}
