using UnityEngine;
using UnityEngine.UI;

// Drop-in replacement for UIController, adapted for SafeCapture streaming.
// Same panel/tab/collapse structure as the Niantic UIController.
public class SafeCaptureUIController : MonoBehaviour
{
    // ── Identity ──────────────────────────────────────────────────────────────

    [SerializeField] private string _title       = "SafeCapture";
    [SerializeField] private string _description = "Privacy-safe live streaming from Quest 3";

    [Header("Header")]
    [SerializeField] private Text _titleText;
    [SerializeField] private Text _descriptionText;

    // ── Panel navigation (mirrors UIController) ───────────────────────────────

    [Header("Panels")]
    [SerializeField] private GameObject _panelsParent;
    [SerializeField] private GameObject _overviewPanel;
    [SerializeField] private GameObject _settingsPanel;
    [SerializeField] private GameObject _infoPanel;

    [Header("Tab Buttons")]
    [SerializeField] private Button _overviewButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _infoButton;
    [SerializeField] private Button _collapseButton;
    [SerializeField] private Button _exitButton;

    // ── Overview panel ────────────────────────────────────────────────────────

    [Header("Overview — Status")]
    [SerializeField] private Text _statusLabel;
    [SerializeField] private Text _ipLabel;
    [SerializeField] private Text _viewerCountLabel;

    [Header("Overview — IP")]
    [SerializeField] private Button _refreshIpButton;

    [Header("Overview — Streaming Controls")]
    [SerializeField] private Button   _connectButton;
    [SerializeField] private Text _connectButtonLabel;
    [SerializeField] private Button   _startStreamButton;
    [SerializeField] private Text _startStreamButtonLabel;

    // ── Settings panel ────────────────────────────────────────────────────────

    [Header("Settings — Frame Capture")]
    [SerializeField] private Slider   _fpsSlider;
    [SerializeField] private Text _fpsValueLabel;
    [SerializeField] private Slider   _qualitySlider;
    [SerializeField] private Text _qualityValueLabel;
    [SerializeField] private FrameCapture _frameCapture;

    // ── Streaming logic ───────────────────────────────────────────────────────

    [Header("Streaming")]
    [SerializeField] private StreamManager _streamManager;

    [Header("Vision")]
    [SerializeField] private Text             _visionStatusLabel;
    [SerializeField] private Text             _detectionLogLabel;
    [SerializeField] private VisionScheduler  _visionScheduler;
    [SerializeField] private Button           _pauseYoloButton;
    [SerializeField] private Text             _pauseYoloLabel;
    [SerializeField] private Button           _pauseSamButton;
    [SerializeField] private Text             _pauseSamLabel;

    [Header("Vision — Timing")]
    [SerializeField] private InputField       _yoloIntervalInput;
    [SerializeField] private Button           _applyYoloTimingButton;
    [SerializeField] private InputField       _samIntervalInput;
    [SerializeField] private Button           _applySamTimingButton;

    [Header("Side Status Panel")]
    [SerializeField] private Text             _sideStatusLabel;

    [Header("Voice Commands")]
    [SerializeField] private VoiceCommandRecognizer _voiceRecognizer;
    private Button _voiceRecordButton;
    private Text _voiceRecordButtonLabel;
    private Text _voiceStatusLabel;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (_titleText       != null) _titleText.text       = _title;
        if (_descriptionText != null) _descriptionText.text = _description;

        _overviewButton?.onClick.AddListener(ShowOverview);
        _settingsButton?.onClick.AddListener(ShowSettings);
        _infoButton?.onClick.AddListener(ShowInfo);
        _collapseButton?.onClick.AddListener(OnCollapseClicked);
        _exitButton?.onClick.AddListener(OnExitClicked);

        _refreshIpButton?.onClick.AddListener(OnRefreshIpClicked);
        _connectButton?.onClick.AddListener(OnConnectClicked);
        _startStreamButton?.onClick.AddListener(OnStartStreamClicked);

        _fpsSlider?.onValueChanged.AddListener(OnFpsChanged);
        _qualitySlider?.onValueChanged.AddListener(OnQualityChanged);

        _pauseYoloButton?.onClick.AddListener(OnPauseYoloClicked);
        _pauseSamButton?.onClick.AddListener(OnPauseSamClicked);
        _applyYoloTimingButton?.onClick.AddListener(OnApplyYoloTimingClicked);
        _applySamTimingButton?.onClick.AddListener(OnApplySamTimingClicked);
        EnsureVoiceControls();
        _voiceRecordButton?.onClick.AddListener(OnVoiceRecordClicked);
    }

    private void OnDisable()
    {
        _overviewButton?.onClick.RemoveListener(ShowOverview);
        _settingsButton?.onClick.RemoveListener(ShowSettings);
        _infoButton?.onClick.RemoveListener(ShowInfo);
        _collapseButton?.onClick.RemoveListener(OnCollapseClicked);
        _exitButton?.onClick.RemoveListener(OnExitClicked);

        _refreshIpButton?.onClick.RemoveListener(OnRefreshIpClicked);
        _connectButton?.onClick.RemoveListener(OnConnectClicked);
        _startStreamButton?.onClick.RemoveListener(OnStartStreamClicked);

        _fpsSlider?.onValueChanged.RemoveListener(OnFpsChanged);
        _qualitySlider?.onValueChanged.RemoveListener(OnQualityChanged);

        _pauseYoloButton?.onClick.RemoveListener(OnPauseYoloClicked);
        _pauseSamButton?.onClick.RemoveListener(OnPauseSamClicked);
        _applyYoloTimingButton?.onClick.RemoveListener(OnApplyYoloTimingClicked);
        _applySamTimingButton?.onClick.RemoveListener(OnApplySamTimingClicked);
        _voiceRecordButton?.onClick.RemoveListener(OnVoiceRecordClicked);
    }

    private void Start()
    {
        // Init sliders from FrameCapture defaults
        if (_frameCapture != null)
        {
            if (_fpsSlider != null)
            {
                _fpsSlider.minValue = 1; _fpsSlider.maxValue = 60;
                _fpsSlider.value = _frameCapture.TargetFps;
                SetFpsLabel(_frameCapture.TargetFps);
            }
            if (_qualitySlider != null)
            {
                _qualitySlider.minValue = 10; _qualitySlider.maxValue = 100;
                _qualitySlider.value = _frameCapture.JpegQuality;
                SetQualityLabel(_frameCapture.JpegQuality);
            }
        }

        // Pre-fill timing inputs with current scheduler values
        if (_visionScheduler != null)
        {
            if (_yoloIntervalInput != null)
                _yoloIntervalInput.text = _visionScheduler.YoloIntervalSec.ToString("F1");
            if (_samIntervalInput != null)
                _samIntervalInput.text  = _visionScheduler.SamVerifyIntervalSec.ToString("F0");
        }

        ShowOverview();
        RefreshUI();
    }

    private void Update() => RefreshUI();

    // ── Tab navigation ────────────────────────────────────────────────────────

    private void ShowOverview()
    {
        SetPanels(overview: true, settings: false, info: false);
        RefreshUI();
    }

    private void ShowSettings()  => SetPanels(overview: false, settings: true,  info: false);
    private void ShowInfo()      => SetPanels(overview: false, settings: false, info: true);

    private void SetPanels(bool overview, bool settings, bool info)
    {
        if (_overviewPanel != null) _overviewPanel.SetActive(overview);
        if (_settingsPanel != null) _settingsPanel.SetActive(settings);
        if (_infoPanel     != null) _infoPanel.SetActive(info);
    }

    private void OnCollapseClicked()
    {
        if (_panelsParent == null) return;
        bool nowActive = !_panelsParent.activeSelf;
        _panelsParent.SetActive(nowActive);
        if (_collapseButton != null)
            _collapseButton.transform.localRotation =
                Quaternion.AngleAxis(nowActive ? 90f : -90f, Vector3.forward);
    }

    private void OnExitClicked() => Application.Quit();

    private void OnApplyYoloTimingClicked()
    {
        if (_visionScheduler == null || _yoloIntervalInput == null) return;
        if (float.TryParse(_yoloIntervalInput.text, out float yolo))
            _visionScheduler.SetYoloInterval(yolo);
        RefreshUI();
    }

    private void OnApplySamTimingClicked()
    {
        if (_visionScheduler == null || _samIntervalInput == null) return;
        if (float.TryParse(_samIntervalInput.text, out float sam))
            _visionScheduler.SetSamInterval(sam);
        RefreshUI();
    }

    private void OnPauseYoloClicked()
    {
        _visionScheduler?.ToggleYolo();
        RefreshUI();
    }

    private void OnPauseSamClicked()
    {
        _visionScheduler?.ToggleSam();
        RefreshUI();
    }

    private void OnVoiceRecordClicked()
    {
        _voiceRecognizer?.ToggleRecording();
        RefreshUI();
    }

    // ── IP refresh ────────────────────────────────────────────────────────────

    private void OnRefreshIpClicked()
    {
        _streamManager?.RefreshIp();
        RefreshUI();
    }

    // ── Streaming button handlers ─────────────────────────────────────────────

    private void OnConnectClicked()
    {
        if (_streamManager == null) return;

        if (_streamManager.CurrentState == StreamManager.StreamState.Idle)
            _streamManager.Connect();
        else
            _streamManager.Disconnect();

        RefreshUI();
    }

    private void OnStartStreamClicked()
    {
        if (_streamManager == null) return;

        if (_streamManager.CurrentState == StreamManager.StreamState.Listening)
            _streamManager.StartStream();
        else if (_streamManager.CurrentState == StreamManager.StreamState.Streaming)
            _streamManager.StopStream();

        RefreshUI();
    }

    // ── Settings handlers ─────────────────────────────────────────────────────

    private void OnFpsChanged(float value)
    {
        int fps = Mathf.RoundToInt(value);
        if (_frameCapture != null) _frameCapture.TargetFps = fps;
        SetFpsLabel(fps);
    }

    private void OnQualityChanged(float value)
    {
        int quality = Mathf.RoundToInt(value);
        if (_frameCapture != null) _frameCapture.JpegQuality = quality;
        SetQualityLabel(quality);
    }

    private void SetFpsLabel(int fps)
    {
        if (_fpsValueLabel != null) _fpsValueLabel.text = $"{fps} fps";
    }

    private void SetQualityLabel(int quality)
    {
        if (_qualityValueLabel != null) _qualityValueLabel.text = $"{quality}%";
    }

    // ── UI refresh ────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        if (_voiceStatusLabel != null && _voiceRecognizer != null)
            _voiceStatusLabel.text = _voiceRecognizer.Status;
        if (_voiceRecordButtonLabel != null && _voiceRecognizer != null)
            _voiceRecordButtonLabel.text = _voiceRecognizer.IsRecording ? "Stop Recording" : "Record Command";
        if (_voiceRecordButton != null && _voiceRecognizer != null)
            _voiceRecordButton.interactable = !_voiceRecognizer.IsBusy || _voiceRecognizer.IsRecording;

        if (_streamManager == null) return;

        var state = _streamManager.CurrentState;

        // Status label
        int frames = _frameCapture != null ? _frameCapture.FrameCount : 0;
        float encMs = _frameCapture != null ? _frameCapture.LastEncodeMs : 0f;
        string status = state switch
        {
            StreamManager.StreamState.Idle      => "Idle",
            StreamManager.StreamState.Listening => "Server ready\nWaiting for viewers...",
            StreamManager.StreamState.Streaming => $"Streaming  •  {_streamManager.ViewerCount} viewer(s)\nFrame #{frames}  |  Encode: {encMs:F1} ms",
            _                                   => ""
        };
        if (_statusLabel != null) _statusLabel.text = status;

        // IP label
        if (_ipLabel != null)
            _ipLabel.text = state == StreamManager.StreamState.Idle
                ? "—"
                : _streamManager.LocalUrl;

        // Vision counter
        if (_visionStatusLabel != null && _visionScheduler != null)
            _visionStatusLabel.text =
                $"yolo - calls:{_visionScheduler.YoloCallCount}  obj:{_visionScheduler.YoloLastObjectCount}  {_visionScheduler.YoloLastLatencyMs:F0}ms\n" +
                $"meta - calls:{_visionScheduler.SamCallCount}  obj:{_visionScheduler.SamLastObjectCount}  next:{_visionScheduler.SamNextAllowedIn:F0}s";

        if (_detectionLogLabel != null && _visionScheduler != null)
            _detectionLogLabel.text = _visionScheduler.StatusLog;

        if (_visionScheduler != null)
        {
            if (_pauseYoloLabel != null)
                _pauseYoloLabel.text = _visionScheduler.YoloPaused ? "Resume YOLO" : "Pause YOLO";
            if (_pauseSamLabel != null)
                _pauseSamLabel.text  = _visionScheduler.SamPaused  ? "Resume Meta" : "Pause Meta";
        }

        // Viewer count
        if (_viewerCountLabel != null)
            _viewerCountLabel.text = state == StreamManager.StreamState.Streaming
                ? $"{_streamManager.ViewerCount} viewer(s) connected"
                : "";

        // Connect button
        if (_connectButtonLabel != null)
            _connectButtonLabel.text = state == StreamManager.StreamState.Idle
                ? "Connect"
                : "Disconnect";

        // Start/Stop stream button
        bool canStream = state != StreamManager.StreamState.Idle;
        if (_startStreamButton != null) _startStreamButton.interactable = canStream;
        if (_startStreamButtonLabel != null)
            _startStreamButtonLabel.text = state == StreamManager.StreamState.Streaming
                ? "Stop Streaming"
                : "Start Streaming";

        // Side status panel
        if (_sideStatusLabel != null)
            _sideStatusLabel.text = BuildSideStatus(state, frames, encMs);
    }

    private string BuildSideStatus(StreamManager.StreamState state, int frames, float encMs)
    {
        var sb = new System.Text.StringBuilder();

        // ── Stream ──
        sb.AppendLine("── Stream ──");
        sb.AppendLine($"State:   {state}");
        if (state == StreamManager.StreamState.Streaming)
        {
            sb.AppendLine($"Frames:  {frames}");
            sb.AppendLine($"Encode:  {encMs:F1} ms");
            sb.AppendLine($"Viewers: {_streamManager.ViewerCount}");
        }

        if (_visionScheduler == null) return sb.ToString();

        // ── YOLO ──
        sb.AppendLine();
        sb.AppendLine("── YOLO ──");
        sb.AppendLine($"Status:   {(_visionScheduler.YoloPaused ? "Paused" : "Running")}");
        sb.AppendLine($"Interval: {_visionScheduler.YoloIntervalSec:F1}s");
        sb.AppendLine($"Calls:    {_visionScheduler.YoloCallCount}");
        sb.AppendLine($"Objects:  {_visionScheduler.YoloLastObjectCount}");
        sb.AppendLine($"Latency:  {_visionScheduler.YoloLastLatencyMs:F0} ms");

        // ── Meta SAM ──
        sb.AppendLine();
        sb.AppendLine("── Meta SAM ──");
        sb.AppendLine($"Status:   {(_visionScheduler.SamPaused ? "Paused" : "Running")}");
        sb.AppendLine($"Interval: {_visionScheduler.SamVerifyIntervalSec:F0}s");
        sb.AppendLine($"Calls:    {_visionScheduler.SamCallCount}");
        sb.AppendLine($"Objects:  {_visionScheduler.SamLastObjectCount}");
        sb.AppendLine($"Next in:  {_visionScheduler.SamNextAllowedIn:F0}s");

        // ── Detections ──
        var result = _visionScheduler.LatestResult;
        if (result?.objects != null && result.objects.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("── Detections ──");
            foreach (var obj in result.objects)
            {
                if (obj == null) continue;
                string state2 = obj.falseDetection ? "✗" : obj.verified ? "✓" : "?";
                sb.AppendLine($"{state2} {obj.label}");
            }
        }

        return sb.ToString();
    }

    private void EnsureVoiceControls()
    {
        if (_voiceRecognizer == null)
            _voiceRecognizer = FindObjectOfType<VoiceCommandRecognizer>();
        if (_voiceRecognizer == null || _voiceRecordButton != null)
            return;

        Transform parent = _statusLabel != null ? _statusLabel.transform.parent : transform;
        var buttonObject = new GameObject("VoiceRecordButton", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button));
        buttonObject.layer = parent.gameObject.layer;
        buttonObject.transform.SetParent(parent, false);
        var buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = new Vector2(20f, -115f);
        buttonRect.sizeDelta = new Vector2(360f, 78f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.04f, 0.13f, 0.25f, 1f);
        _voiceRecordButton = buttonObject.GetComponent<Button>();
        _voiceRecordButton.targetGraphic = image;
        Font voiceFont = _statusLabel != null ? _statusLabel.font : null;
        _voiceRecordButtonLabel = CreateVoiceText(
            "VoiceRecordButtonLabel", buttonRect, "Record Command", 32, TextAnchor.MiddleCenter,
            new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, voiceFont);

        _voiceStatusLabel = CreateVoiceText(
            "VoiceCommandStatus", parent, _voiceRecognizer.Status, 26, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -205f), new Vector2(1000f, 100f),
            voiceFont);
        RefreshUI();
    }

    private static Text CreateVoiceText(
        string objectName, Transform parent, string initialText, int fontSize, TextAnchor alignment,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Font font)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = anchorMin;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = textObject.GetComponent<Text>();
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = initialText;
        return text;
    }
}
