using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class FrameCapture : MonoBehaviour
{
    // ── Settings ──────────────────────────────────────────────────────────────

    [Header("Passthrough Camera (Quest 3)")]
    [Tooltip("Use Quest 3's physical camera via WebCamTexture. Requires camera permission.")]
    [SerializeField] private bool _usePassthrough = false;
    [Tooltip("Camera device index. On Quest 3 try 0 first; if monochrome try 4 or 5 for color cameras.")]
    [SerializeField] private int _cameraIndex = 0;

    [Header("Render Camera (Editor / fallback)")]
    [SerializeField] private Camera _captureCamera;

    [Header("Stream Quality")]
    [SerializeField] private int _targetFps   = 24;
    [SerializeField] private int _jpegQuality = 75;

    // ── Public API ────────────────────────────────────────────────────────────

    public int TargetFps
    {
        get => _targetFps;
        set { _targetFps = Mathf.Clamp(value, 1, 60); _interval = 1f / _targetFps; }
    }

    public int JpegQuality
    {
        get => _jpegQuality;
        set => _jpegQuality = Mathf.Clamp(value, 10, 100);
    }

    public byte[]  LatestFrame   { get; private set; }
    public bool    HasNewFrame   { get; private set; }
    public int     FrameCount    { get; private set; }
    public float   LastEncodeMs  { get; private set; }
    public void    ConsumeFrame() => HasNewFrame = false;

    public int CaptureWidth  => _readbackTex != null ? _readbackTex.width  : 0;
    public int CaptureHeight => _readbackTex != null ? _readbackTex.height : 0;

    // Subscribers (e.g. BoxOverlay) may draw on the texture before JPEG encoding.
    public event System.Action<Texture2D> PreEncodeFilter;

    // ── Private state ─────────────────────────────────────────────────────────

    // Render-camera path
    private RenderTexture _rt;
    private Texture2D     _readbackTex;
    private bool          _asyncReadbackPending;

    // WebCamTexture passthrough path
    private WebCamTexture _webcam;
    private RenderTexture _webcamRT;

    private float _interval;
    private float _nextCaptureTime;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        _interval = 1f / Mathf.Max(1, _targetFps);

        if (_usePassthrough)
            InitPassthrough();
        else
            InitRenderCapture();
    }

    private void Update()
    {
        if (_usePassthrough)
            UpdatePassthrough();
        else
            UpdateRenderCapture();
    }

    private void OnDestroy()
    {
        if (_usePassthrough)
        {
            if (_webcam      != null) { _webcam.Stop(); Destroy(_webcam); }
            if (_webcamRT    != null) Destroy(_webcamRT);
            if (_readbackTex != null) Destroy(_readbackTex);
        }
        else
        {
            if (_captureCamera != null && _captureCamera.targetTexture == _rt)
                _captureCamera.targetTexture = null;
            if (_rt          != null) Destroy(_rt);
            if (_readbackTex != null) Destroy(_readbackTex);
        }
    }

    // ── Passthrough path ──────────────────────────────────────────────────────

    private void InitPassthrough()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            var cb = new PermissionCallbacks();
            cb.PermissionGranted += _ => StartCoroutine(StartWebcamNextFrame());
            cb.PermissionDenied  += s =>
            {
                Debug.LogWarning($"[FrameCapture] Camera permission denied: {s}. Falling back to render.");
                _usePassthrough = false;
                InitRenderCapture();
            };
            Permission.RequestUserPermission(Permission.Camera, cb);
        }
        else
        {
            StartCoroutine(StartWebcamNextFrame());
        }
#else
        StartCoroutine(StartWebcamNextFrame());
#endif
    }

    // Delay by one frame so the XR runtime is fully initialized before Camera2 is touched.
    private IEnumerator StartWebcamNextFrame()
    {
        yield return null;

        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"[FrameCapture] Found {devices.Length} camera device(s).");
        for (int i = 0; i < devices.Length; i++)
            Debug.Log($"[FrameCapture] Camera {i}: '{devices[i].name}' frontFacing={devices[i].isFrontFacing}");

        if (devices.Length == 0)
        {
            Debug.LogWarning("[FrameCapture] No WebCam devices found. Falling back to render capture.");
            _usePassthrough = false;
            InitRenderCapture();
            yield break;
        }

        int idx = Mathf.Clamp(_cameraIndex, 0, devices.Length - 1);
        _webcam   = new WebCamTexture(devices[idx].name, 1280, 720, _targetFps);
        _webcamRT = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
        _readbackTex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        _webcam.Play();
        Debug.Log($"[FrameCapture] WebCam started: index={idx} name='{devices[idx].name}'");
    }

    private void UpdatePassthrough()
    {
        if (_webcam == null || !_webcam.isPlaying || !_webcam.didUpdateThisFrame) return;
        if (_asyncReadbackPending) return;
        if (Time.time < _nextCaptureTime) return;

        _nextCaptureTime = Time.time + _interval;

        if (_webcamRT.width != _webcam.width || _webcamRT.height != _webcam.height)
        {
            _webcamRT.Release();
            _webcamRT    = new RenderTexture(_webcam.width, _webcam.height, 0, RenderTextureFormat.ARGB32);
            _readbackTex = new Texture2D(_webcam.width, _webcam.height, TextureFormat.RGB24, false);
            Debug.Log($"[FrameCapture] Webcam actual res: {_webcam.width}x{_webcam.height}");
        }

        Graphics.Blit(_webcam, _webcamRT);

        if (SystemInfo.supportsAsyncGPUReadback)
        {
            _asyncReadbackPending = true;
            AsyncGPUReadback.Request(_webcamRT, 0, TextureFormat.RGB24, OnReadbackComplete);
        }
        else
        {
            SyncReadback(_webcamRT);
        }
    }

    // ── Render-camera path ────────────────────────────────────────────────────

    private void InitRenderCapture()
    {
        int w = Mathf.Min(_captureCamera != null ? _captureCamera.pixelWidth  : 1280, 1280);
        int h = Mathf.Min(_captureCamera != null ? _captureCamera.pixelHeight : 720,  720);

        _rt          = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        _readbackTex = new Texture2D(w, h, TextureFormat.RGB24, false);

        if (_captureCamera != null)
            _captureCamera.targetTexture = _rt;
    }

    private void UpdateRenderCapture()
    {
        if (_asyncReadbackPending) return;
        if (Time.time < _nextCaptureTime) return;
        if (_captureCamera == null) return;

        _nextCaptureTime = Time.time + _interval;

        if (SystemInfo.supportsAsyncGPUReadback)
        {
            _asyncReadbackPending = true;
            AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGB24, OnReadbackComplete);
        }
        else
        {
            SyncReadback(_rt);
        }
    }

    // ── Shared readback / encode ──────────────────────────────────────────────

    private void OnReadbackComplete(AsyncGPUReadbackRequest request)
    {
        _asyncReadbackPending = false;
        if (request.hasError) return;

        NativeArray<byte> raw = request.GetData<byte>();
        _readbackTex.LoadRawTextureData(raw);
        _readbackTex.Apply();
        EncodeAndPublish();
    }

    private void SyncReadback(RenderTexture source)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = source;
        _readbackTex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
        _readbackTex.Apply();
        RenderTexture.active = prev;
        EncodeAndPublish();
    }

    private void EncodeAndPublish()
    {
        try
        {
            PreEncodeFilter?.Invoke(_readbackTex);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            LatestFrame = ImageConversion.EncodeArrayToJPG(
                _readbackTex.GetRawTextureData(),
                GraphicsFormat.R8G8B8_UNorm,
                (uint)_readbackTex.width, (uint)_readbackTex.height,
                0, _jpegQuality);
            sw.Stop();
            LastEncodeMs = (float)sw.Elapsed.TotalMilliseconds;
            HasNewFrame  = true;
            FrameCount++;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FrameCapture] EncodeAndPublish failed: {e.Message}");
        }
        finally
        {
            _asyncReadbackPending = false;
        }
    }
}
