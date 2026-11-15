using UnityEngine;

// Draws bounding boxes + labels onto the stream frame before JPEG encoding.
// Cyan = user selected   Green = SAM verified   Yellow = unverified (YOLO only)
[DefaultExecutionOrder(100)]
public class BoxOverlay : MonoBehaviour, IDetectionConsumer
{
    [SerializeField] private FrameCapture _frameCapture;
    [SerializeField] private int          _labelScale = 2;

    private static readonly Color Verified   = Color.green;
    private static readonly Color Unverified = Color.yellow;
    private static readonly Color BgColor    = new Color(0f, 0f, 0f, 1f);
    private static readonly Color TextColor  = Color.white;

    private VisionResult _latest;
    private ObjectSelectionManager _selectionManager;
    private readonly object _lock = new object();

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
        lock (_lock) _latest = result;
    }

    private void Draw(Texture2D tex)
    {
        VisionResult result;
        lock (_lock) result = _latest;
        if (result?.objects == null || result.objects.Length == 0) return;

        foreach (var obj in result.objects)
        {
            bool selected = _selectionManager != null && _selectionManager.IsSelected(obj.track_id);
            if (obj.falseDetection && !selected) continue;

            var boxColor = selected ? Color.cyan : (obj.verified ? Verified : Unverified);

            int x1  = Mathf.Clamp((int)obj.box.x1, 0, tex.width  - 1);
            int x2  = Mathf.Clamp((int)obj.box.x2, 0, tex.width  - 1);
            int ty1 = Mathf.Clamp(tex.height - 1 - (int)obj.box.y2, 0, tex.height - 1);
            int ty2 = Mathf.Clamp(tex.height - 1 - (int)obj.box.y1, 0, tex.height - 1);

            DrawRect(tex, x1, x2, ty1, ty2, boxColor);

            // Label above box — baseline of label = ty2 + 1
            string tag = selected
                ? "SELECTED " + obj.label
                : (obj.verified ? obj.label : obj.label + "?");
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
