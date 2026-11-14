using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class FrameCapture : MonoBehaviour
{
    [SerializeField] private Camera _captureCamera;
    [SerializeField] private int _targetFps = 10;
    [SerializeField] private int _jpegQuality = 50;

    public byte[] LatestFrame { get; private set; }
    public bool HasNewFrame { get; private set; }

    private RenderTexture _rt;
    private Texture2D _readbackTex;
    private float _interval;
    private float _nextCaptureTime;
    private bool _asyncReadbackPending;

    private void Start()
    {
        _interval = 1f / Mathf.Max(1, _targetFps);

        int w = _captureCamera != null ? _captureCamera.pixelWidth  : 1024;
        int h = _captureCamera != null ? _captureCamera.pixelHeight : 512;

        _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        _readbackTex = new Texture2D(w, h, TextureFormat.RGB24, mipChain: false);

        if (_captureCamera != null)
            _captureCamera.targetTexture = _rt;
    }

    private void Update()
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
            SyncReadback();
        }
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request)
    {
        _asyncReadbackPending = false;
        if (request.hasError) return;

        NativeArray<byte> raw = request.GetData<byte>();
        _readbackTex.LoadRawTextureData(raw);
        _readbackTex.Apply();
        EncodeAndPublish();
    }

    private void SyncReadback()
    {
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        _readbackTex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0, recalculateMipMaps: false);
        _readbackTex.Apply();
        RenderTexture.active = prev;
        EncodeAndPublish();
    }

    private void EncodeAndPublish()
    {
        LatestFrame = _readbackTex.EncodeToJPG(_jpegQuality);
        HasNewFrame = true;
    }

    // Called by MjpegServer after it has consumed the frame
    public void ConsumeFrame() => HasNewFrame = false;

    private void OnDestroy()
    {
        if (_captureCamera != null && _captureCamera.targetTexture == _rt)
            _captureCamera.targetTexture = null;

        if (_rt != null) Destroy(_rt);
        if (_readbackTex != null) Destroy(_readbackTex);
    }
}
