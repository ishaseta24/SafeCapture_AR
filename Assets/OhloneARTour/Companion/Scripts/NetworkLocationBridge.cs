using NianticSpatial.NSDK.AR.Loader;
using UnityEngine;

public class NetworkLocationBridge : MonoBehaviour
{
    private const string Tag = "[NetworkLocationBridge]";

    [SerializeField] private GPSReceiver _gpsReceiver;
    [SerializeField] private VPSSpoofTimestampBridge _timestampBridge;

    [Tooltip("Seconds without a packet before falling back to spoof GPS")]
    [SerializeField] private float _timeoutSeconds = 5f;

    private int _lastAppliedPacket = -1;

    private void Update()
    {
        if (_gpsReceiver == null || _timestampBridge == null) return;

        float timeSinceLastPacket = Time.realtimeSinceStartup - _gpsReceiver.LastPacketTime;
        bool hasRecentPacket = _gpsReceiver.PacketCount > 0 && timeSinceLastPacket <= _timeoutSeconds;

        if (!hasRecentPacket)
        {
            // No companion signal — let VPSSpoofTimestampBridge handle coordinates
            if (_timestampBridge.UseExternalGPS)
            {
                _timestampBridge.UseExternalGPS = false;
                Debug.Log($"{Tag} Companion signal lost ({timeSinceLastPacket:F1}s ago) — falling back to spoof GPS.");
            }
            return;
        }

        var packet = _gpsReceiver.LastPacket;
        if (packet == null || _gpsReceiver.PacketCount == _lastAppliedPacket) return;

        _lastAppliedPacket = _gpsReceiver.PacketCount;

        var settings = NsdkSettingsHelper.ActiveSettings;
        if (settings == null) return;

        settings.SpoofLocationInfo.Latitude = packet.Latitude;
        settings.SpoofLocationInfo.Longitude = packet.Longitude;

        _timestampBridge.UseExternalGPS = true;

        Debug.Log($"{Tag} Applied phone GPS — lat={packet.Latitude:F6}, lon={packet.Longitude:F6}");
    }
}
