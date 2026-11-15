using UnityEngine;
using UnityEngine.UI;

public class SafeCaptureUIController : MonoBehaviour
{
    [SerializeField] private string _title       = "SafeCapture";
    [SerializeField] private string _description = "Privacy-safe live streaming from Quest 3";

    [Header("Header")]
    [SerializeField] private Text _titleText;
    [SerializeField] private Text _descriptionText;

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

    [Header("Overview — Status")]
    [SerializeField] private Text _statusLabel;
    [SerializeField] private Text _ipLabel;
    [SerializeField] private Text _viewerCountLabel;

    [Header("Overview — IP")]
    [SerializeField] private Button _refreshIpButton;

    [Header("Overview — Streaming Controls")]
    [SerializeField] private Button _connectButton;
    [SerializeField] private Text   _connectButtonLabel;
    [SerializeField] private Button _startStreamButton;
    [SerializeField] private Text   _startStreamButtonLabel;

    [Header("Settings — Frame Capture")]
    [SerializeField] private Slider _fpsSlider;
    [SerializeField] private Text   _fpsValueLabel;
    [SerializeField] private Slider _qualitySlider;
    [SerializeField] private Text   _qualityValueLabel;
    [SerializeField] private FrameCapture _frameCapture;

    [Header("Streaming")]
    [SerializeField] private StreamManager _streamManager;

    [Header("Vision")]
    [SerializeField] private Text            _visionStatusLabel;
    [SerializeField] private Text            _detectionLogLabel;
    [SerializeField] private VisionScheduler _visionScheduler;
    [SerializeField] private Button          _pauseYoloButton;
    [SerializeField] private Text            _pauseYoloLabel;
    [SerializeField] private Button          _pauseSamButton;
    [SerializeField] private Text            _pauseSamLabel;

    [Header("Vision — Timing")]
    [SerializeField] private Text   _yoloIntervalDisplay;
    [SerializeField] private Button _yoloDecButton;
    [SerializeField] private Button _yoloIncButton;
    [SerializeField] private Text   _samIntervalDisplay;
    [SerializeField] private Button _samDecButton;
    [SerializeField] private Button _samIncButton;

    [Header("Voice")]
    [SerializeField] private VoiceCommandRecognizer _voiceRecognizer;
    [SerializeField] private Button                 _micButton;
    [SerializeField] private Text                   _micButtonLabel;
    [SerializeField] private Text                   _voiceFeedbackLabel;

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

        _yoloDecButton?.onClick.AddListener(OnYoloDecClicked);
        _yoloIncButton?.onClick.AddListener(OnYoloIncClicked);
        _samDecButton?.onClick.AddListener(OnSamDecClicked);
        _samIncButton?.onClick.AddListener(OnSamIncClicked);

        _micButton?.onClick.AddListener(OnMicToggleClicked);

        if (_voiceRecognizer != null)
            _voiceRecognizer.StatusChanged += OnVoiceStatus;
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

        _yoloDecButton?.onClick.RemoveListener(OnYoloDecClicked);
        _yoloIncButton?.onClick.RemoveListener(OnYoloIncClicked);
        _samDecButton?.onClick.RemoveListener(OnSamDecClicked);
        _samIncButton?.onClick.RemoveListener(OnSamIncClicked);

        _micButton?.onClick.RemoveListener(OnMicToggleClicked);

        if (_voiceRecognizer != null)
            _voiceRecognizer.StatusChanged -= OnVoiceStatus;
    }

    private void Start()
    {
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

        if (_voiceFeedbackLabel != null && _voiceRecognizer != null)
            _voiceFeedbackLabel.text = _voiceRecognizer.Status;

        ShowOverview();
        RefreshUI();
    }

    private void Update() => RefreshUI();

    // ── Tab navigation ────────────────────────────────────────────────────────

    private void ShowOverview()   => SetPanels(true,  false, false);
    private void ShowSettings()   => SetPanels(false, true,  false);
    private void ShowInfo()       => SetPanels(false, false, true);

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

    // ── Timing +/- handlers ───────────────────────────────────────────────────

    private void OnYoloDecClicked()
    {
        if (_visionScheduler == null) return;
        _visionScheduler.SetYoloInterval(_visionScheduler.YoloIntervalSec + 0.1f);
        RefreshUI();
    }

    private void OnYoloIncClicked()
    {
        if (_visionScheduler == null) return;
        _visionScheduler.SetYoloInterval(Mathf.Max(0.1f, _visionScheduler.YoloIntervalSec - 0.1f));
        RefreshUI();
    }

    private void OnSamDecClicked()
    {
        if (_visionScheduler == null) return;
        _visionScheduler.SetSamInterval(_visionScheduler.SamVerifyIntervalSec + 1f);
        RefreshUI();
    }

    private void OnSamIncClicked()
    {
        if (_visionScheduler == null) return;
        _visionScheduler.SetSamInterval(Mathf.Max(1f, _visionScheduler.SamVerifyIntervalSec - 1f));
        RefreshUI();
    }

    // ── Voice (tap to record / tap to stop) ──────────────────────────────────

    private void OnMicToggleClicked()
    {
        if (_voiceRecognizer == null) return;
        if (_voiceRecognizer.IsRecording)
        {
            _voiceRecognizer.StopRecordingAndTranscribe();
            if (_micButtonLabel != null) _micButtonLabel.text = "Record";
        }
        else if (!_voiceRecognizer.IsBusy)
        {
            _voiceRecognizer.StartRecording();
            if (_micButtonLabel != null) _micButtonLabel.text = "■ Stop";
        }
    }

    private void OnVoiceStatus(string status)
    {
        if (_voiceFeedbackLabel != null) _voiceFeedbackLabel.text = status;
    }

    // ── Vision pause buttons ──────────────────────────────────────────────────

    private void OnPauseYoloClicked() { _visionScheduler?.ToggleYolo(); RefreshUI(); }
    private void OnPauseSamClicked()  { _visionScheduler?.ToggleSam();  RefreshUI(); }

    // ── IP / stream handlers ──────────────────────────────────────────────────

    private void OnRefreshIpClicked() { _streamManager?.RefreshIp(); RefreshUI(); }

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

    // ── Settings sliders ──────────────────────────────────────────────────────

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

    private void SetFpsLabel(int fps)   { if (_fpsValueLabel    != null) _fpsValueLabel.text    = $"{fps} fps"; }
    private void SetQualityLabel(int q) { if (_qualityValueLabel != null) _qualityValueLabel.text = $"{q}%"; }

    // ── UI refresh ────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        if (_streamManager == null) return;

        var   state  = _streamManager.CurrentState;
        int   frames = _frameCapture != null ? _frameCapture.FrameCount   : 0;
        float encMs  = _frameCapture != null ? _frameCapture.LastEncodeMs : 0f;

        if (_statusLabel != null) _statusLabel.text = BuildStatus(state, frames, encMs);

        if (_ipLabel != null)
            _ipLabel.text = state == StreamManager.StreamState.Idle ? "—" : _streamManager.LocalUrl;

        if (_visionStatusLabel != null && _visionScheduler != null)
            _visionStatusLabel.text =
                $"yolo  calls:{_visionScheduler.YoloCallCount}  obj:{_visionScheduler.YoloLastObjectCount}  {_visionScheduler.YoloLastLatencyMs:F0}ms\n" +
                $"meta  calls:{_visionScheduler.SamCallCount}  obj:{_visionScheduler.SamLastObjectCount}  next:{_visionScheduler.SamNextAllowedIn:F0}s";

        if (_detectionLogLabel != null && _visionScheduler != null)
            _detectionLogLabel.text = _visionScheduler.StatusLog;

        if (_visionScheduler != null)
        {
            if (_pauseYoloLabel != null) _pauseYoloLabel.text = _visionScheduler.YoloPaused ? "Resume YOLO" : "Pause YOLO";
            if (_pauseSamLabel  != null) _pauseSamLabel.text  = _visionScheduler.SamPaused  ? "Resume Meta" : "Pause Meta";
            if (_yoloIntervalDisplay != null) _yoloIntervalDisplay.text = $"{_visionScheduler.YoloIntervalSec:F1}s";
            if (_samIntervalDisplay  != null) _samIntervalDisplay.text  = $"{_visionScheduler.SamVerifyIntervalSec:F0}s";
        }

        if (_viewerCountLabel != null)
            _viewerCountLabel.text = state == StreamManager.StreamState.Streaming
                ? $"{_streamManager.ViewerCount} viewer(s) connected" : "";

        if (_connectButtonLabel != null)
            _connectButtonLabel.text = state == StreamManager.StreamState.Idle ? "Connect" : "Disconnect";

        bool canStream = state != StreamManager.StreamState.Idle;
        if (_startStreamButton != null) _startStreamButton.interactable = canStream;
        if (_startStreamButtonLabel != null)
            _startStreamButtonLabel.text = state == StreamManager.StreamState.Streaming
                ? "Stop Streaming" : "Start Streaming";

        // Keep mic label in sync (don't overwrite while actively recording — OnMicToggleClicked handles it)
        if (_micButtonLabel != null && _voiceRecognizer != null && !_voiceRecognizer.IsRecording)
            _micButtonLabel.text = _voiceRecognizer.IsBusy ? "Processing…" : "Record";
    }

    private string BuildStatus(StreamManager.StreamState state, int frames, float encMs)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("── Stream ──");
        sb.AppendLine($"State:   {state}");
        if (state == StreamManager.StreamState.Streaming)
        {
            sb.AppendLine($"Frames:  {frames}");
            sb.AppendLine($"Encode:  {encMs:F1} ms");
            sb.AppendLine($"Viewers: {_streamManager.ViewerCount}");
        }

        if (_visionScheduler == null) return sb.ToString();

        sb.AppendLine();
        sb.AppendLine("── YOLO ──");
        sb.AppendLine($"Status:   {(_visionScheduler.YoloPaused ? "Paused" : "Running")}");
        sb.AppendLine($"Interval: {_visionScheduler.YoloIntervalSec:F1}s");
        sb.AppendLine($"Calls:    {_visionScheduler.YoloCallCount}");
        sb.AppendLine($"Objects:  {_visionScheduler.YoloLastObjectCount}");
        sb.AppendLine($"Latency:  {_visionScheduler.YoloLastLatencyMs:F0} ms");

        sb.AppendLine();
        sb.AppendLine("── Meta SAM ──");
        sb.AppendLine($"Status:   {(_visionScheduler.SamPaused ? "Paused" : "Running")}");
        sb.AppendLine($"Interval: {_visionScheduler.SamVerifyIntervalSec:F0}s");
        sb.AppendLine($"Calls:    {_visionScheduler.SamCallCount}");
        sb.AppendLine($"Objects:  {_visionScheduler.SamLastObjectCount}");
        sb.AppendLine($"Next in:  {_visionScheduler.SamNextAllowedIn:F0}s");

        var result = _visionScheduler.LatestResult;
        if (result?.objects != null && result.objects.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("── Detections ──");
            foreach (var obj in result.objects)
            {
                if (obj == null) continue;
                string mark = obj.falseDetection ? "✗" : obj.verified ? "✓" : "?";
                sb.AppendLine($"{mark} {obj.label}");
            }
        }

        return sb.ToString();
    }
}
