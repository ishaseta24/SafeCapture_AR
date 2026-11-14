#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class QuestLocationProvider : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text gpsDisplay;

    [Header("Location Settings")]
    public float desiredAccuracyMeters = 10f;
    public float updateDistanceMeters = 10f;
    public float updateIntervalSeconds = 5f;
    public int maxInitializationWaitSeconds = 20;

    private bool isLocationServiceRunning = false;
    private Coroutine locationLoopCoroutine;

    private void Start()
    {
        SetText("Starting location setup...");
        StartCoroutine(InitializeLocationService());
    }

    private IEnumerator InitializeLocationService()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        SetText("Requesting location permission...");

        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(2f);
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
        {
            Permission.RequestUserPermission(Permission.CoarseLocation);
            yield return new WaitForSeconds(2f);
        }

        bool hasFinePermission = Permission.HasUserAuthorizedPermission(Permission.FineLocation);
        bool hasCoarsePermission = Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);

        Debug.Log("[QuestLocation] Fine permission: " + hasFinePermission);
        Debug.Log("[QuestLocation] Coarse permission: " + hasCoarsePermission);

        if (!hasFinePermission && !hasCoarsePermission)
        {
            Debug.LogWarning("[QuestLocation] Location permission denied.");
            SetText("Location permission denied");
            yield break;
        }
#endif

        SetText("Checking location service...");

        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("[QuestLocation] Location service is disabled.");
            SetText("Location service disabled");
            yield break;
        }

        Debug.Log("[QuestLocation] Starting location service...");
        SetText("Starting location service...");

        Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);

        int waitTime = maxInitializationWaitSeconds;

        while (Input.location.status == LocationServiceStatus.Initializing && waitTime > 0)
        {
            Debug.Log("[QuestLocation] Initializing... " + waitTime);
            SetText("Initializing location... " + waitTime);
            yield return new WaitForSeconds(1f);
            waitTime--;
        }

        if (waitTime <= 0)
        {
            Debug.LogError("[QuestLocation] Location timeout.");
            SetText("Location timeout");
            yield break;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("[QuestLocation] Location failed.");
            SetText("Location failed");
            yield break;
        }

        if (Input.location.status == LocationServiceStatus.Running)
        {
            isLocationServiceRunning = true;

            Debug.Log("[QuestLocation] Location service running.");
            SetText("Location service running...");

            locationLoopCoroutine = StartCoroutine(LocationUpdateLoop());
        }
        else
        {
            Debug.LogWarning("[QuestLocation] Unknown location status: " + Input.location.status);
            SetText("Location status: " + Input.location.status);
        }
    }

    private IEnumerator LocationUpdateLoop()
    {
        while (isLocationServiceRunning)
        {
            LogCurrentLocation();
            yield return new WaitForSeconds(updateIntervalSeconds);
        }
    }

    private void LogCurrentLocation()
    {
        if (!isLocationServiceRunning || Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning("[QuestLocation] Location unavailable. Status: " + Input.location.status);
            SetText("Location unavailable\nStatus: " + Input.location.status);
            return;
        }

        LocationInfo data = Input.location.lastData;
        string pacificTime = GetPacificTimeString();

        string msg =
            $"Latitude: {data.latitude:F6}\n" +
            $"Longitude: {data.longitude:F6}\n" +
            $"Accuracy: {data.horizontalAccuracy:F2} m\n" +
            $"Altitude: {data.altitude:F2} m\n" +
            $"PST/Pacific Time: {pacificTime}";

        Debug.Log("[QuestLocation]\n" + msg);
        SetText(msg);
    }

    private string GetPacificTimeString()
    {
        try
        {
            TimeZoneInfo pacificTimeZone;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Android / Quest usually uses IANA timezone IDs
            pacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
#else
            // macOS/Linux also usually supports this
            pacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
#endif

            DateTime pacificTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pacificTimeZone);
            return pacificTime.ToString("yyyy-MM-dd hh:mm:ss tt");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[QuestLocation] Could not get Pacific timezone. Using local time. " + e.Message);
            return DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
        }
    }

    private void SetText(string text)
    {
        if (gpsDisplay != null)
        {
            gpsDisplay.text = text;
        }
    }

    private void OnApplicationPause(bool pause)
    {
        Debug.Log("[QuestLocation] App pause: " + pause);
        SetText("App pause: " + pause);
    }

    private void OnApplicationFocus(bool focus)
    {
        Debug.Log("[QuestLocation] App focus: " + focus);
        SetText("App focus: " + focus);
    }

    private void OnDisable()
    {
        StopLocationService();
    }

    private void OnDestroy()
    {
        StopLocationService();
    }

    private void StopLocationService()
    {
        isLocationServiceRunning = false;

        if (locationLoopCoroutine != null)
        {
            StopCoroutine(locationLoopCoroutine);
            locationLoopCoroutine = null;
        }

        if (Input.location.status == LocationServiceStatus.Running)
        {
            Input.location.Stop();
            Debug.Log("[QuestLocation] Location service stopped.");
        }
    }
}