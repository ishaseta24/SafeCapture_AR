// Copyright 2022-2026 Niantic.

using System;
using System.Collections;
using System.Reflection;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using NianticSpatial.NSDK.AR;
using NianticSpatial.NSDK.AR.Loader;
using UnityEngine;

// Aligns spoofed GNSS/heading timestamps with the camera's monotonic boot-time clock,
// and feeds real device coordinates (via Unity's location service) into SpoofLocationInfo.
//
// Root cause of timestamp mismatch: NSDK Settings uses LocationDataSource.Spoof so the
// Quest (which has no GPS chip) can provide a fixed lat/lon to VPS. The default spoof
// timestamp is 123456 s (→ 123456000 ms), while the camera's OpenXR frame timestamp is
// nanoseconds since device boot (→ ~62731598 ms at 17 h of uptime). The NSDK frame
// synchronizer receives GNSS/heading at 123456000 ms and camera frames at ~62731xxx ms
// and cannot match them across clock domains, so VPS localization never fires.
//
// Fix (timestamp): use Android's SystemClock.elapsedRealtime() via JNI to read
// milliseconds-since-boot directly — the same clock OpenXR camera frames use.
//
// Stale reference problem: SpoofCompassProvider captures _spoofInfo at field-init time;
// SpoofLocationServiceProvider.Start() captures it when NSDK calls location.Start().
// Both can run independently of this bridge's Start(), so after CopyFrom() or a
// reordering the providers hold stale objects. Fix: in Update(), call SetValue every
// frame to forcibly sync both providers to the CURRENT ActiveSettings objects before
// writing timestamps. Two SetValue calls/frame is negligible on Quest hardware.
//
// Fix (coordinates): start Unity's Input.location service (Quest uses WiFi/network
// positioning) and copy the real lat/lon into SpoofLocationInfo so SitesClientDemo finds
// VPS sites near the actual device position. Falls back to _fallbackLatitude/Longitude
// if the location service cannot start or returns implausible coords.
//
// Add this component to any persistent GameObject in the VPS2Localization scene.
public class VPSSpoofTimestampBridge : MonoBehaviour
{
    private const string Tag = "[VPSSpoofTimestampBridge]";

    [Header("Timestamp")]
    [Tooltip("How often (seconds) to log the periodic timestamp update. 0 = every frame.")]
    [SerializeField] private float _logIntervalSeconds = 5f;

    [Header("Location Fallback (used if Quest location service fails or returns implausible coords)")]
    [Tooltip("Fallback latitude if device location unavailable (Ohlone College, Fremont)")]
    [SerializeField] private float _fallbackLatitude = 37.5726f;
    [Tooltip("Fallback longitude if device location unavailable (Ohlone College, Fremont)")]
    [SerializeField] private float _fallbackLongitude = -121.9860f;
    [Tooltip("Seconds to wait for location service to initialize")]
    [SerializeField] private int _locationInitTimeoutSeconds = 20;
    [Tooltip("Max km a WiFi fix may be from the fallback coords before it is rejected as implausible")]
    [SerializeField] private float _maxDistanceFromFallbackKm = 10f;

    // Set to true by NetworkLocationBridge when phone GPS is actively providing coordinates.
    // Prevents this bridge from overwriting phone GPS with Quest WiFi fix.
    public bool UseExternalGPS { get; set; }

    private float _nextLogTime;
    private bool _locationServiceStarted;
    private bool _locationReady;
    private bool _usingFallbackCoords;

    // Reflection: cached at Start(). SetValue called every Update() frame to keep both
    // providers pointing at the current ActiveSettings objects.
    private FieldInfo _compassSpoofInfoField;
    private object _compassProvider;

    private FieldInfo _locationSpoofInfoField;
    private object _locationProvider;

    private readonly WaitForSeconds _wait1s = new(1f);
#if UNITY_ANDROID && !UNITY_EDITOR
    private readonly WaitForSeconds _wait2s = new(2f);
#endif

    private void Awake()
    {
        Debug.Log($"{Tag} Awake — will calibrate timestamps and start Quest location service.");
    }

    private void Start()
    {
        var settings = NsdkSettingsHelper.ActiveSettings;
        if (settings == null)
        {
            Debug.LogWarning($"{Tag} NsdkSettingsHelper.ActiveSettings is NULL. Cannot inspect spoof config.");
            return;
        }

        Debug.Log($"{Tag} LocationAndCompassDataSource = {settings.LocationAndCompassDataSource}");
        Debug.Log($"{Tag} Initial SpoofLocationInfo: lat={settings.SpoofLocationInfo.Latitude}, " +
                  $"lon={settings.SpoofLocationInfo.Longitude}, timestamp={settings.SpoofLocationInfo.Timestamp}");
        Debug.Log($"{Tag} Initial SpoofCompassInfo: timestamp={settings.SpoofCompassInfo.Timestamp}");
        Debug.Log($"{Tag} Fallback coords: lat={_fallbackLatitude}, lon={_fallbackLongitude}");

        if (settings.LocationAndCompassDataSource != LocationDataSource.Spoof)
            Debug.LogWarning($"{Tag} DataSource is not Spoof ({settings.LocationAndCompassDataSource}). This bridge will be inactive.");

        ResolveProviderFields();

        _nextLogTime = Time.realtimeSinceStartup + _logIntervalSeconds;
        StartCoroutine(InitLocationService());
    }

    // Cache FieldInfo, provider objects, and the provider's current _spoofInfo references.
    // Cache FieldInfo objects and provider instances so Update() can call SetValue
    // every frame without repeated GetType().GetField() lookups.
    private void ResolveProviderFields()
    {
        try
        {
            var nsdkCompass = NianticSpatial.NSDK.AR.Input.compass;
            var compassProviderField = typeof(NianticSpatial.NSDK.AR.Compass)
                .GetField("_spoofProvider", BindingFlags.NonPublic | BindingFlags.Instance);
            _compassProvider = compassProviderField?.GetValue(nsdkCompass);
            _compassSpoofInfoField = _compassProvider?.GetType()
                .GetField("_spoofInfo", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_compassSpoofInfoField != null)
                Debug.Log($"{Tag} Compass: provider resolved — will sync _spoofInfo every frame.");
            else
                Debug.LogWarning($"{Tag} Could not resolve SpoofCompassProvider._spoofInfo — heading TS will not reach PAM.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{Tag} Compass reflection failed: {ex.Message}");
        }

        try
        {
            var nsdkLocation = NianticSpatial.NSDK.AR.Input.location;
            var locationProviderField = typeof(NianticSpatial.NSDK.AR.LocationService)
                .GetField("_spoofProvider", BindingFlags.NonPublic | BindingFlags.Instance);
            _locationProvider = locationProviderField?.GetValue(nsdkLocation);
            _locationSpoofInfoField = _locationProvider?.GetType()
                .GetField("_spoofInfo", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_locationSpoofInfoField != null)
                Debug.Log($"{Tag} Location: provider resolved — will sync _spoofInfo every frame.");
            else
                Debug.LogWarning($"{Tag} Could not resolve SpoofLocationServiceProvider._spoofInfo — GNSS TS will not reach PAM.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{Tag} Location reflection failed: {ex.Message}");
        }
    }

    private IEnumerator InitLocationService()
    {
        Debug.Log($"{Tag} [Location] Starting Quest location service (WiFi/network positioning)...");

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Debug.Log($"{Tag} [Location] Requesting FineLocation permission...");
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return _wait2s;
        }
        if (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
        {
            Debug.Log($"{Tag} [Location] Requesting CoarseLocation permission...");
            Permission.RequestUserPermission(Permission.CoarseLocation);
            yield return _wait2s;
        }

        bool hasFine = Permission.HasUserAuthorizedPermission(Permission.FineLocation);
        bool hasCoarse = Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);
        Debug.Log($"{Tag} [Location] FineLocation={hasFine}, CoarseLocation={hasCoarse}");

        if (!hasFine && !hasCoarse)
        {
            Debug.LogWarning($"{Tag} [Location] Permission denied — falling back to lat={_fallbackLatitude}, lon={_fallbackLongitude}");
            ApplyFallbackCoords();
            yield break;
        }
#endif

        if (!UnityEngine.Input.location.isEnabledByUser)
        {
            Debug.LogWarning($"{Tag} [Location] Location disabled by user — falling back.");
            ApplyFallbackCoords();
            yield break;
        }

        UnityEngine.Input.location.Start(10f, 10f);
        _locationServiceStarted = true;
        Debug.Log($"{Tag} [Location] Input.location.Start() called. Status={UnityEngine.Input.location.status}");

        int wait = _locationInitTimeoutSeconds;
        while (UnityEngine.Input.location.status == LocationServiceStatus.Initializing && wait > 0)
        {
            Debug.Log($"{Tag} [Location] Initializing... ({wait}s remaining)");
            yield return _wait1s;
            wait--;
        }

        Debug.Log($"{Tag} [Location] Init complete. Status={UnityEngine.Input.location.status}");

        if (UnityEngine.Input.location.status == LocationServiceStatus.Running)
        {
            _locationReady = true;
            LocationInfo d = UnityEngine.Input.location.lastData;
            Debug.Log($"{Tag} [Location] First fix: lat={d.latitude:F6}, lon={d.longitude:F6}, accuracy={d.horizontalAccuracy:F1}m");
            ApplySpoofCoords(d.latitude, d.longitude, d.altitude, d.horizontalAccuracy);
        }
        else if (UnityEngine.Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogWarning($"{Tag} [Location] Service failed — falling back.");
            ApplyFallbackCoords();
        }
        else
        {
            Debug.LogWarning($"{Tag} [Location] Timed out (status={UnityEngine.Input.location.status}) — falling back.");
            ApplyFallbackCoords();
        }
    }

    private void Update()
    {
        var settings = NsdkSettingsHelper.ActiveSettings;
        if (settings == null) return;

        if (settings.LocationAndCompassDataSource != LocationDataSource.Spoof)
        {
            if (Time.realtimeSinceStartup >= _nextLogTime)
            {
                Debug.Log($"{Tag} Update skipped — DataSource is {settings.LocationAndCompassDataSource}, not Spoof.");
                _nextLogTime = Time.realtimeSinceStartup + _logIntervalSeconds;
            }
            return;
        }

        // Force-sync providers to the current settings objects every frame.
        // SpoofLocationServiceProvider.Start() and SpoofCompassProvider's field-init
        // can both run after bridge.Start() and grab different object references.
        // SetValue here ensures providers always read from the same objects we update below.
        _compassSpoofInfoField?.SetValue(_compassProvider, settings.SpoofCompassInfo);
        _locationSpoofInfoField?.SetValue(_locationProvider, settings.SpoofLocationInfo);

        double bootSeconds = BootTimeSecs();
        double prevGnssTs = settings.SpoofLocationInfo.Timestamp;
        double prevCompassTs = settings.SpoofCompassInfo.Timestamp;

        settings.SpoofLocationInfo.Timestamp = bootSeconds;
        settings.SpoofCompassInfo.Timestamp = bootSeconds;

        // --- Live coordinate updates (skipped when phone GPS is active via NetworkLocationBridge) ---
        if (!UseExternalGPS && _locationReady && UnityEngine.Input.location.status == LocationServiceStatus.Running)
        {
            LocationInfo d = UnityEngine.Input.location.lastData;
            if (IsValidCoord(d.latitude, d.longitude))
            {
                settings.SpoofLocationInfo.Latitude = d.latitude;
                settings.SpoofLocationInfo.Longitude = d.longitude;
                settings.SpoofLocationInfo.Altitude = float.IsNaN(d.altitude) ? 0f : d.altitude;
                settings.SpoofLocationInfo.HorizontalAccuracy = d.horizontalAccuracy;
            }
        }

        if (Time.realtimeSinceStartup < _nextLogTime) return;

        double bootMs = bootSeconds * 1000.0;
        string coordSource = _usingFallbackCoords ? "FALLBACK"
            : UseExternalGPS ? "PHONE GPS"
            : (_locationReady ? "Quest WiFi" : "pending");
        Debug.Log(
            $"{Tag} Timestamp update —\n" +
            $"  Boot-time ms: {bootMs:F0}\n" +
            $"  GNSS:    {prevGnssTs * 1000.0:F0} ms → {bootMs:F0} ms\n" +
            $"  Heading: {prevCompassTs * 1000.0:F0} ms → {bootMs:F0} ms\n" +
            $"  Coords [{coordSource}]: lat={settings.SpoofLocationInfo.Latitude:F6}, " +
            $"lon={settings.SpoofLocationInfo.Longitude:F6}\n" +
            $"  LocationService status: {UnityEngine.Input.location.status}"
        );
        _nextLogTime = Time.realtimeSinceStartup + _logIntervalSeconds;
    }

    // Returns seconds since device boot using Android's SystemClock.elapsedRealtime()
    // — same clock domain as OpenXR camera frame timestamps.
    private static double BootTimeSecs()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using var systemClock = new AndroidJavaClass("android.os.SystemClock");
        return systemClock.CallStatic<long>("elapsedRealtime") / 1000.0;
#else
        return Time.realtimeSinceStartupAsDouble;
#endif
    }

    private void ApplySpoofCoords(float lat, float lon, float altitude, float accuracy)
    {
        if (!IsValidCoord(lat, lon))
        {
            Debug.LogWarning($"{Tag} [Location] Ignoring invalid coords lat={lat}, lon={lon} — falling back.");
            ApplyFallbackCoords();
            return;
        }
        float distKm = HaversineKm(lat, lon, _fallbackLatitude, _fallbackLongitude);
        if (distKm > _maxDistanceFromFallbackKm)
        {
            Debug.LogWarning($"{Tag} [Location] WiFi fix ({lat:F4}, {lon:F4}) is {distKm:F1} km from fallback — too far, using fallback.");
            ApplyFallbackCoords();
            return;
        }
        var settings = NsdkSettingsHelper.ActiveSettings;
        if (settings == null) return;
        settings.SpoofLocationInfo.Latitude = lat;
        settings.SpoofLocationInfo.Longitude = lon;
        settings.SpoofLocationInfo.Altitude = float.IsNaN(altitude) ? 0f : altitude;
        settings.SpoofLocationInfo.HorizontalAccuracy = accuracy;
        _usingFallbackCoords = false;
        Debug.Log($"{Tag} [Location] Updated — lat={lat:F6}, lon={lon:F6}, accuracy={accuracy:F1}m, {distKm:F1}km from fallback");
    }

    private void ApplyFallbackCoords()
    {
        var settings = NsdkSettingsHelper.ActiveSettings;
        if (settings == null) return;
        settings.SpoofLocationInfo.Latitude = _fallbackLatitude;
        settings.SpoofLocationInfo.Longitude = _fallbackLongitude;
        _usingFallbackCoords = true;
        Debug.Log($"{Tag} [Location] Fallback applied — lat={_fallbackLatitude:F6}, lon={_fallbackLongitude:F6} (Ohlone College)");
    }

    private static bool IsValidCoord(float lat, float lon) =>
        !float.IsNaN(lat) && !float.IsNaN(lon) && (lat != 0f || lon != 0f);

    private static float HaversineKm(float lat1, float lon1, float lat2, float lon2)
    {
        const float R = 6371f;
        float dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        float dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2)
                + Mathf.Cos(lat1 * Mathf.Deg2Rad) * Mathf.Cos(lat2 * Mathf.Deg2Rad)
                * Mathf.Sin(dLon / 2) * Mathf.Sin(dLon / 2);
        return R * 2f * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1f - a));
    }

    private void OnDisable()
    {
        Debug.Log($"{Tag} OnDisable — timestamp updates paused.");
    }

    private void OnDestroy()
    {
        if (_locationServiceStarted && UnityEngine.Input.location.status == LocationServiceStatus.Running)
        {
            UnityEngine.Input.location.Stop();
            Debug.Log($"{Tag} [Location] Input.location stopped on destroy.");
        }
        Debug.Log($"{Tag} OnDestroy — bridge destroyed.");
    }
}
