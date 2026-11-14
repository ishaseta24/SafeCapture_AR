using System.Net;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Attach to the SafeCapture GameObject.
// Wire _capture, _server, buttons, and labels in the Inspector.
public class StreamManager : MonoBehaviour
{
    [Header("Subsystems")]
    [SerializeField] private FrameCapture _capture;
    [SerializeField] private MjpegServer  _server;

    [Header("Buttons")]
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _startStreamButton;

    [Header("Labels")]
    [SerializeField] private TMP_Text _statusLabel;
    [SerializeField] private TMP_Text _connectButtonLabel;
    [SerializeField] private TMP_Text _startStreamButtonLabel;

    private enum State { Idle, Listening, Streaming }
    private State _state = State.Idle;
    private string _localUrl;

    private void Awake()
    {
        // Both subsystems start inactive; we enable them on demand
        if (_capture != null) _capture.enabled = false;
        if (_server  != null) _server.enabled  = false;

        _connectButton?.onClick.AddListener(OnConnectClicked);
        _startStreamButton?.onClick.AddListener(OnStartStreamClicked);
    }

    private void Start()
    {
        _localUrl = $"http://{GetLocalIPAddress()}:{NetworkConfig.StreamPort}/";
        SetState(State.Idle);
    }

    private void Update()
    {
        // Keep status label fresh with live viewer count while streaming
        if (_state == State.Streaming && _server != null)
            SetStatusText($"Streaming\n{_localUrl}\n{_server.ConnectedClientCount} viewer(s)");
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnConnectClicked()
    {
        if (_state == State.Idle)
        {
            if (_server != null) _server.enabled = true;
            SetState(State.Listening);
        }
        else if (_state == State.Listening || _state == State.Streaming)
        {
            // Disconnect: stop everything and return to Idle
            if (_capture != null) _capture.enabled = false;
            if (_server  != null) _server.enabled  = false;
            SetState(State.Idle);
        }
    }

    private void OnStartStreamClicked()
    {
        if (_state == State.Listening)
        {
            if (_capture != null) _capture.enabled = true;
            SetState(State.Streaming);
        }
        else if (_state == State.Streaming)
        {
            if (_capture != null) _capture.enabled = false;
            SetState(State.Listening);
        }
    }

    // ── State helpers ─────────────────────────────────────────────────────────

    private void SetState(State next)
    {
        _state = next;

        switch (next)
        {
            case State.Idle:
                SetStatusText("Idle\nTap Connect to start the server.");
                SetButtonLabel(_connectButtonLabel, "Connect");
                SetButtonLabel(_startStreamButtonLabel, "Start Streaming");
                SetButtonInteractable(_startStreamButton, false);
                break;

            case State.Listening:
                SetStatusText($"Server ready\n{_localUrl}\nWaiting for viewers...");
                SetButtonLabel(_connectButtonLabel, "Disconnect");
                SetButtonLabel(_startStreamButtonLabel, "Start Streaming");
                SetButtonInteractable(_startStreamButton, true);
                break;

            case State.Streaming:
                // Label updated every frame in Update()
                SetButtonLabel(_connectButtonLabel, "Disconnect");
                SetButtonLabel(_startStreamButtonLabel, "Stop Streaming");
                SetButtonInteractable(_startStreamButton, true);
                break;
        }
    }

    private void SetStatusText(string text)
    {
        if (_statusLabel != null) _statusLabel.text = text;
    }

    private static void SetButtonLabel(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }

    private static void SetButtonInteractable(Button btn, bool interactable)
    {
        if (btn != null) btn.interactable = interactable;
    }

    // ── IP resolution ─────────────────────────────────────────────────────────

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
