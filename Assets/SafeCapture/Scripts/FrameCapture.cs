using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using Meta.XR;
#endif

public class FrameCapture : MonoBehaviour
{
    // ── Settings ──────────────────────────────────────────────────────────────

    [Header("Passthrough Camera (Quest 3)")]
    [Tooltip("Use Quest 3's physical camera via PassthroughCameraAccess (MR Utility Kit). Assign _passCam below.")]
    [SerializeField] private bool _usePassthrough = false;

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

    public event Action<Texture2D> PreEncodeFilter;

    // ── Private state ─────────────────────────────────────────────────────────

    private RenderTexture _rt;          // render-capture path only
    private Texture2D     _readbackTex;
    private bool          _asyncReadbackPending;

#if UNITY_ANDROID && !UNITY_EDITOR
    // PassthroughCameraAccess — Meta's official Quest 3 passthrough camera API.
    // Lives on a sibling GameObject; we find or create it at startup.
    // Requires horizonos.permission.HEADSET_CAMERA in AndroidManifest.xml.
    private PassthroughCameraAccess _passCam;
    private bool _passCamReadyLogged;
#else
    // Editor: WebCamTexture fallback
    [SerializeField] private int _cameraIndex = 0;
    private WebCamTexture _webcam;
    private RenderTexture _webcamRT;
#endif

    private float _interval;
    private float _nextCaptureTime;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        _interval = 1f / Mathf.Max(1, _targetFps);
        if (_usePassthrough) InitPassthrough();
        else                 InitRenderCapture();
    }

    private void Update()
    {
        if (_usePassthrough) UpdatePassthrough();
        else                 UpdateRenderCapture();
    }

    private void OnDestroy()
    {
        if (_usePassthrough)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_passCam != null)
            {
                _passCam.enabled = false;
                // Don't Destroy — may be on a shared GameObject
            }
#else
            if (_webcam != null) { _webcam.Stop(); Destroy(_webcam); }
            if (_webcamRT != null) Destroy(_webcamRT);
#endif
        }
        else
        {
            if (_captureCamera != null && _captureCamera.targetTexture == _rt)
                _captureCamera.targetTexture = null;
            if (_rt != null) Destroy(_rt);
        }
        if (_readbackTex != null) Destroy(_readbackTex);
    }

    // ── Passthrough path ──────────────────────────────────────────────────────

    private void InitPassthrough()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(OpenPassthroughCameraCoroutine());
#else
        StartCoroutine(StartWebcamNextFrame());
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator OpenPassthroughCameraCoroutine()
    {
        // 2 frames for the XR runtime to come up, then 1 s for the passthrough
        // subsystem to initialize before MRUKNativeFuncs.CameraPlay() is called.
        yield return null;
        yield return null;
        yield return new WaitForSeconds(1f);

        _passCam = GetComponentInChildren<PassthroughCameraAccess>(true);
        if (_passCam == null)
        {
            _passCam = gameObject.AddComponent<PassthroughCameraAccess>();
            // AddComponent fires Awake+OnEnable with defaults (Left camera).
            // Disable immediately so we can reconfigure before re-enabling.
            _passCam.enabled = false;
        }

        // Right eye = index 1 (confirmed working on this device).
        _passCam.CameraPosition      = PassthroughCameraAccess.CameraPositionType.Right;
        _passCam.RequestedResolution = new Vector2Int(1280, 960);
        _passCam.enabled = true;

        Debug.Log("[FrameCapture] PassthroughCameraAccess enabled (Right). Waiting for IsPlaying…");
    }
#endif

    private void UpdatePassthrough()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_passCam == null || !_passCam.IsPlaying) return;
        if (!_passCam.IsUpdatedThisFrame) return;
        if (_asyncReadbackPending) return;
        if (Time.time < _nextCaptureTime) return;

        if (!_passCamReadyLogged)
        {
            _passCamReadyLogged = true;
            Debug.Log($"[FrameCapture] PassthroughCameraAccess playing @ {_passCam.CurrentResolution}");
        }

        _nextCaptureTime = Time.time + _interval;

        var tex = _passCam.GetTexture();
        if (tex == null) return;

        // Lazily create or resize the CPU-side readback texture.
        if (_readbackTex == null || _readbackTex.width != tex.width || _readbackTex.height != tex.height)
        {
            if (_readbackTex != null) Destroy(_readbackTex);
            _readbackTex = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
        }

        // GetTexture() returns a RenderTexture — AsyncGPUReadback works directly on it.
        if (SystemInfo.supportsAsyncGPUReadback)
        {
            _asyncReadbackPending = true;
            AsyncGPUReadback.Request(tex, 0, TextureFormat.RGB24, OnReadbackComplete);
        }
        else
        {
            SyncReadback((RenderTexture)tex);
        }
#else
        UpdateWebcam();
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // No separate WebCam path on Android.
#else
    // ── Editor: WebCamTexture path ────────────────────────────────────────────

    private IEnumerator StartWebcamNextFrame()
    {
        yield return null;
        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"[FrameCapture] Found {devices.Length} camera device(s).");
        for (int i = 0; i < devices.Length; i++)
            Debug.Log($"[FrameCapture] Camera {i}: '{devices[i].name}'");
        if (devices.Length == 0)
        {
            Debug.LogWarning("[FrameCapture] No WebCam devices. Falling back to render.");
            _usePassthrough = false; InitRenderCapture(); yield break;
        }
        int idx      = Mathf.Clamp(_cameraIndex, 0, devices.Length - 1);
        _webcam      = new WebCamTexture(devices[idx].name, 1280, 720, _targetFps);
        _webcamRT    = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
        _readbackTex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        _webcam.Play();
        Debug.Log($"[FrameCapture] WebCam started: index={idx} name='{devices[idx].name}'");
    }

    private void UpdateWebcam()
    {
        if (_webcam == null || !_webcam.isPlaying || !_webcam.didUpdateThisFrame) return;
        if (_asyncReadbackPending) return;
        if (Time.time < _nextCaptureTime) return;
        _nextCaptureTime = Time.time + _interval;
        if (_webcamRT == null || _webcamRT.width != _webcam.width || _webcamRT.height != _webcam.height)
        {
            if (_webcamRT != null) _webcamRT.Release();
            _webcamRT    = new RenderTexture(_webcam.width, _webcam.height, 0, RenderTextureFormat.ARGB32);
            if (_readbackTex != null) Destroy(_readbackTex);
            _readbackTex = new Texture2D(_webcam.width, _webcam.height, TextureFormat.RGB24, false);
        }
        Graphics.Blit(_webcam, _webcamRT);
        if (SystemInfo.supportsAsyncGPUReadback)
        { _asyncReadbackPending = true; AsyncGPUReadback.Request(_webcamRT, 0, TextureFormat.RGB24, OnReadbackComplete); }
        else SyncReadback(_webcamRT);
    }
#endif

    // ── Render-camera path ────────────────────────────────────────────────────

    private void InitRenderCapture()
    {
        int w = Mathf.Min(_captureCamera != null ? _captureCamera.pixelWidth  : 1280, 1280);
        int h = Mathf.Min(_captureCamera != null ? _captureCamera.pixelHeight : 720,  720);
        _rt          = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        _readbackTex = new Texture2D(w, h, TextureFormat.RGB24, false);
        if (_captureCamera != null) _captureCamera.targetTexture = _rt;
    }

    private void UpdateRenderCapture()
    {
        if (_asyncReadbackPending) return;
        if (Time.time < _nextCaptureTime) return;
        if (_captureCamera == null) return;
        _nextCaptureTime = Time.time + _interval;
        if (SystemInfo.supportsAsyncGPUReadback)
        { _asyncReadbackPending = true; AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGB24, OnReadbackComplete); }
        else SyncReadback(_rt);
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
