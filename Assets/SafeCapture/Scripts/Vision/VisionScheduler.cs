using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

// Fires DetectAsync every detectIntervalSec; at most one request in flight.
public class VisionScheduler : MonoBehaviour
{
    [SerializeField] private FrameCapture _frameCapture;

    public int   CallCount       { get; private set; }
    public int   LastObjectCount { get; private set; }
    public float NextCallIn      { get; private set; }

    private IVisionProvider _provider;
    private bool            _inFlight;
    private float           _nextCallTime;

    private void Start()
    {
        _provider = new SamRelayProvider();
        StartCoroutine(ScheduleLoop());
    }

    private IEnumerator ScheduleLoop()
    {
        float interval = Mathf.Max(0.5f, AppConfig.Instance.detectIntervalSec);
        while (true)
        {
            _nextCallTime = Time.time + interval;
            yield return new WaitForSeconds(interval);
            if (!_inFlight && _frameCapture != null && _frameCapture.LatestFrame != null)
                _ = Dispatch(_frameCapture.LatestFrame, _frameCapture.CaptureWidth, _frameCapture.CaptureHeight);
        }
    }

    private void Update()
    {
        NextCallIn = Mathf.Max(0f, _nextCallTime - Time.time);
    }

    private async Task Dispatch(byte[] frame, int w, int h)
    {
        _inFlight = true;
        try
        {
            var result = await _provider.DetectAsync(frame, w, h);
            if (result == null) return;

            CallCount++;
            LastObjectCount = result.objects?.Length ?? 0;
            Debug.Log($"[VisionScheduler] frame={result.frame_id} latency={result.latency_ms}ms objects={LastObjectCount}");
            if (result.objects != null)
                foreach (var obj in result.objects)
                    Debug.Log($"  [{obj.label}] ({obj.box.x1},{obj.box.y1})-({obj.box.x2},{obj.box.y2})");

            foreach (var consumer in GetComponents<IDetectionConsumer>())
                consumer.OnDetectionResult(result);
        }
        catch (Exception e)
        {
            Debug.LogError($"[VisionScheduler] {e.Message}");
        }
        finally
        {
            _inFlight = false;
        }
    }
}
