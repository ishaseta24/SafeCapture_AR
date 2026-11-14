using UnityEngine;
using UnityEngine.UI;

public class PairingCodeDisplay : MonoBehaviour
{
    [SerializeField] private QuestDiscoveryBroadcaster _broadcaster;
    [SerializeField] private GPSReceiver _gpsReceiver;

    [Header("UI")]
    [SerializeField] private Text _codeText;
    [SerializeField] private Text _ipText;
    [SerializeField] private Text _statusText;
    [SerializeField] private Text _timerText;
    [SerializeField] private Text _gpsDataText;
    [SerializeField] private Button _refreshButton;
    [SerializeField] private Button _refreshIPButton;

    private const float ConnectionTimeoutSeconds = 5f;

    private void Start()
    {
        if (_refreshButton != null)
            _refreshButton.onClick.AddListener(OnRefreshClicked);
        if (_refreshIPButton != null)
            _refreshIPButton.onClick.AddListener(OnRefreshIPClicked);
    }

    private void Update()
    {
        if (_broadcaster != null)
        {
            if (_codeText != null)
                _codeText.text = $"Code: {_broadcaster.PairingCode}";

            if (_ipText != null)
                _ipText.text = $"IP: {_broadcaster.IP}";

            if (_timerText != null)
            {
                float t = _broadcaster.TimeUntilRefresh;
                _timerText.text = t >= 0f ? $"Refresh in: {t:F0}s" : "";
            }
        }

        if (_statusText != null && _gpsReceiver != null)
        {
            float timeSincePacket = Time.realtimeSinceStartup - _gpsReceiver.LastPacketTime;
            int count = _gpsReceiver.PacketCount;

            if (count == 0)
                _statusText.text = "Waiting for companion...";
            else if (timeSincePacket <= ConnectionTimeoutSeconds)
                _statusText.text = $"Connected - GPS #{count}";
            else
                _statusText.text = $"Signal lost ({timeSincePacket:F0}s ago) - #{count} packets";

            if (_gpsDataText != null)
            {
                var p = _gpsReceiver.LastPacket;
                if (p != null)
                    _gpsDataText.text = $"Lat: {p.Latitude:F6}\nLon: {p.Longitude:F6}\nTimestamp: {p.TimestampMs}\nNote: {p.Note}";
                else
                    _gpsDataText.text = "No data yet";
            }
        }
    }

    private void OnRefreshClicked()
    {
        if (_broadcaster != null)
            _broadcaster.RefreshCode();
    }

    private void OnRefreshIPClicked()
    {
        if (_broadcaster != null)
            _broadcaster.RefreshIP();
    }

    private void OnDestroy()
    {
        if (_refreshButton != null)
            _refreshButton.onClick.RemoveListener(OnRefreshClicked);
        if (_refreshIPButton != null)
            _refreshIPButton.onClick.RemoveListener(OnRefreshIPClicked);
    }
}
