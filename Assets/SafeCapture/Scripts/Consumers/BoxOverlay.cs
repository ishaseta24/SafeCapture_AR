using UnityEngine;

// Draws bounding boxes onto the stream frame before JPEG encoding.
// Attach to the same GameObject as VisionScheduler.
public class BoxOverlay : MonoBehaviour, IDetectionConsumer
{
    [SerializeField] private FrameCapture _frameCapture;

    private VisionResult _latest;
    private readonly object _lock = new object();

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
            DrawRect(tex, obj.box, Color.green);

        tex.Apply(false);
    }

    private static void DrawRect(Texture2D tex, BoundingBox box, Color color)
    {
        int x1  = Mathf.Clamp((int)box.x1, 0, tex.width  - 1);
        int x2  = Mathf.Clamp((int)box.x2, 0, tex.width  - 1);
        // Unity texture origin is bottom-left; flip Y to match top-left frame coords
        int ty1 = Mathf.Clamp(tex.height - 1 - (int)box.y2, 0, tex.height - 1);
        int ty2 = Mathf.Clamp(tex.height - 1 - (int)box.y1, 0, tex.height - 1);

        for (int x = x1; x <= x2; x++) { tex.SetPixel(x, ty1, color); tex.SetPixel(x, ty2, color); }
        for (int y = ty1; y <= ty2; y++) { tex.SetPixel(x1, y, color); tex.SetPixel(x2, y, color); }
    }
}
