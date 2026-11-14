// Quest-only narration/captions controller for World Space Canvas.
// Stripped of all touch input and screen-space assumptions from the mobile Captions.cs.
// Buttons call public methods directly (wired in Inspector or via XR Interaction Toolkit's
// interactable OnClick events — no EventSystem touch required).

using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuestCaptions : MonoBehaviour
{
    [Header("Narration")]
    [SerializeField] private AudioSource _narrationAudio;

    [Header("Transcript")]
    [Tooltip("Path relative to StreamingAssets, e.g. 'Fountain/narration.txt'")]
    [SerializeField] private string _narrationTextPath;
    [SerializeField] private TMP_Text _narrationText;
    [SerializeField] private RectTransform _narrationTextTransform;
    [SerializeField] private RectTransform _narrationViewport;

    [Header("Controls")]
    [SerializeField] private Image _playPauseIcon;
    [SerializeField] private Sprite _playSprite;
    [SerializeField] private Sprite _pauseSprite;
    [SerializeField] private Slider _progressSlider;

    [Header("Speaker Info")]
    [SerializeField] private TMP_Text _narratorNameText;
    [SerializeField] private TMP_Text _narratorRoleText;
    [SerializeField] private TMP_Text _stopNameText;
    [SerializeField] private Image _narratorImage;
    [SerializeField] private Sprite _narratorSprite;
    [SerializeField] private string _narratorName = "Monica V. Arellano";
    [SerializeField] private string _narratorRole = "Ohlone Community Member";
    [SerializeField] private string _stopName = "University Center Fountain";

    private string[] _lines;
    private float _duration;
    private bool _isPlaying;
    private bool _autoScroll = true;
    private float _maxScroll;

    private void OnEnable()
    {
        ApplySpeakerInfo();
    }

    // Called by FountainStopManager.OpenCaptions()
    public void StartNarration(AudioSource audio)
    {
        _narrationAudio = audio;
        LoadTranscript();
        PlayAudio();
    }

    private void LoadTranscript()
    {
        if (string.IsNullOrEmpty(_narrationTextPath)) return;

        string path = Path.Combine(Application.streamingAssetsPath, _narrationTextPath);
        if (File.Exists(path))
        {
            string text = File.ReadAllText(path);
            _lines = text.Split('\n');
            _narrationText.text = text;
        }
        else
        {
            Debug.LogWarning($"[QuestCaptions] Transcript not found at {path}");
        }
    }

    private void Update()
    {
        if (_narrationAudio == null || !_narrationAudio.isPlaying) return;

        float progress = _narrationAudio.time / _duration;

        // Update progress slider
        if (_progressSlider != null)
            _progressSlider.value = progress;

        // Bold current line in transcript
        if (_lines != null && _lines.Length > 0)
        {
            int currentLine = Mathf.Clamp((int)(_narrationAudio.time / 7f), 0, _lines.Length - 1);
            string display = "";
            for (int i = 0; i < _lines.Length; i++)
                display += i == currentLine ? $"<b>{_lines[i]}</b> " : $"{_lines[i]} ";
            _narrationText.text = display;
        }

        // Auto-scroll transcript
        if (_autoScroll && _narrationTextTransform != null && _narrationViewport != null)
        {
            _maxScroll = _narrationTextTransform.rect.height - _narrationViewport.rect.height;
            Vector2 pos = _narrationTextTransform.anchoredPosition;
            pos.y = progress * _maxScroll;
            _narrationTextTransform.anchoredPosition = pos;
        }

        // Swap icon when audio ends
        if (!_narrationAudio.isPlaying && _isPlaying)
        {
            _isPlaying = false;
            SetIcon(_playSprite);
        }
    }

    // Wired to Play/Pause button OnClick in Inspector
    public void TogglePlayPause()
    {
        if (_narrationAudio == null) return;

        if (_isPlaying)
        {
            _narrationAudio.Pause();
            _isPlaying = false;
            SetIcon(_playSprite);
        }
        else
        {
            _narrationAudio.UnPause();
            _isPlaying = true;
            _autoScroll = true;
            SetIcon(_pauseSprite);
        }
    }

    // Wired to the progress Slider OnValueChanged in Inspector (user drags)
    public void OnSliderDragged()
    {
        if (_narrationAudio == null || _progressSlider == null) return;
        _narrationAudio.time = _progressSlider.value * _duration;
        _autoScroll = false;
    }

    // Wired to scroll rect OnEndDrag so manual scroll stops auto-scroll
    public void OnManualScroll() => _autoScroll = false;

    private void PlayAudio()
    {
        if (_narrationAudio == null || _narrationAudio.clip == null) return;
        _narrationAudio.Play();
        _duration = _narrationAudio.clip.length;
        _isPlaying = true;
        _autoScroll = true;
        SetIcon(_pauseSprite);
    }

    private void SetIcon(Sprite sprite)
    {
        if (_playPauseIcon != null && sprite != null)
            _playPauseIcon.sprite = sprite;
    }

    private void ApplySpeakerInfo()
    {
        if (_narratorNameText != null) _narratorNameText.text = "As narrated by " + _narratorName;
        if (_narratorRoleText != null) _narratorRoleText.text = _narratorRole;
        if (_stopNameText != null) _stopNameText.text = _stopName;
        if (_narratorImage != null && _narratorSprite != null) _narratorImage.sprite = _narratorSprite;
    }
}
