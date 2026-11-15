using System.Net;
using System.Net.Sockets;
using UnityEngine;

// Pure streaming logic — no UI. SafeCaptureUIController drives all state changes.
public class StreamManager : MonoBehaviour
{
    [SerializeField] private FrameCapture _capture;
    [SerializeField] private MjpegServer  _server;

    public enum StreamState { Idle, Listening, Streaming }
    public StreamState CurrentState { get; private set; } = StreamState.Idle;

    public string LocalUrl    { get; private set; } = "";
    public int    ViewerCount => _server != null ? _server.ConnectedClientCount : 0;

    private void Awake()
    {
        if (_capture != null) _capture.enabled = false;
        if (_server  != null) _server.enabled  = false;

        LocalUrl = $"http://{GetLocalIPAddress()}:{NetworkConfig.StreamPort}/";
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void RefreshIp()
    {
        LocalUrl = $"http://{GetLocalIPAddress()}:{NetworkConfig.StreamPort}/";
        Debug.Log($"[StreamManager] IP refreshed → {LocalUrl}");
    }

    public void Connect()
    {
        if (CurrentState != StreamState.Idle) return;
        if (_server != null) _server.enabled = true;
        CurrentState = StreamState.Listening;
        Debug.Log($"[StreamManager] Server listening at {LocalUrl}");
    }

    public void Disconnect()
    {
        if (_capture != null) _capture.enabled = false;
        if (_server  != null) _server.enabled  = false;
        CurrentState = StreamState.Idle;
        Debug.Log("[StreamManager] Disconnected.");
    }

    public void StartStream()
    {
        if (CurrentState != StreamState.Listening) return;
        if (_capture != null) _capture.enabled = true;
        CurrentState = StreamState.Streaming;
        Debug.Log("[StreamManager] Streaming started.");
    }

    public void StopStream()
    {
        if (CurrentState != StreamState.Streaming) return;
        if (_capture != null) _capture.enabled = false;
        CurrentState = StreamState.Listening;
        Debug.Log("[StreamManager] Streaming stopped.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetLocalIPAddress()
    {
        try
        {
            foreach (var addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                    return addr.ToString();
            }
        }
        catch (SocketException ex)
        {
            Debug.LogWarning($"[StreamManager] Could not resolve local IP: {ex.Message}");
        }
        return "127.0.0.1";
    }
}
