using UnityEngine;

// Placeholder — blurs detected regions in the stream frame (step 5).
// Enable _active after BoxOverlay is verified correct.
public class PrivacyBlur : MonoBehaviour, IDetectionConsumer
{
    [SerializeField] private FrameCapture _frameCapture;
    [SerializeField] private bool         _active = false;

    private VisionResult _latest;
    private readonly object _lock = new object();

    private void OnEnable()
    {
        if (_active && _frameCapture != null) _frameCapture.PreEncodeFilter += Blur;
    }

    private void OnDisable()
    {
        if (_frameCapture != null) _frameCapture.PreEncodeFilter -= Blur;
    }

    public void OnDetectionResult(VisionResult result)
    {
        lock (_lock) _latest = result;
    }

    private void Blur(Texture2D tex)
    {
        // TODO(step 5): box-region blur
    }
}
