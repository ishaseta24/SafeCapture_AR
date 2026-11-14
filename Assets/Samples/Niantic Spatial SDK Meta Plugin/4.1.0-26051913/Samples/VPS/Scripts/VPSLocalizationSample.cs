// Copyright 2022-2026 Niantic Spatial.

using System.Collections.Generic;
using System.Linq;
using Niantic.Lightship.AR.Protobuf;
using NianticSpatial.NSDK.AR;
using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.LocationAR;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.XRSubsystems;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Input = NianticSpatial.NSDK.AR.Input;
using NetworkRequestStatus = Niantic.Lightship.AR.Protobuf.NetworkRequestStatus;
using TrackingState = UnityEngine.XR.ARSubsystems.TrackingState;

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    public class VPSLocalizationSample : MonoBehaviour
    {
        [SerializeField, Tooltip("The Location Manager")]
        private ARLocationManager _arLocationManager;

        [Header("UI")]
        [SerializeField, Tooltip("The Dropdown for Persistent AR Locations")]
        private Dropdown _arLocationDropdown;

        [SerializeField, Tooltip("The Button to select an AR Location")]
        private Button _localizeButton;

        [SerializeField, Tooltip("The UI Canvas to display the AR Location Selector")]
        private GameObject _arLocationUI;

        [SerializeField]
        private Text _localizationStatusDisplayText;

        [SerializeField]
        private Text _gpsDisplayText;

        [SerializeField]
        private Text _debugDisplayText;

        private readonly List<ARLocation> _arLocationsDropdownItems = new();
        private bool _localizationStatus;

        private void OnEnable()
        {
            _localizeButton.onClick.AddListener(LocalizeButton_OnClick);
            _arLocationManager.trackablesChanged.AddListener(OnAnchorChanged);

        }

        private void OnDisable()
        {
            _localizeButton.onClick.RemoveListener(LocalizeButton_OnClick);
            _arLocationManager.trackablesChanged.RemoveListener(OnAnchorChanged);

        }

        private void Start()
        {
            // Verify settings
            if (NsdkSettingsHelper.ActiveSettings.LocationAndCompassDataSource != LocationDataSource.Spoof)
            {
                Debug.LogError
                (
                    "Meta Quest does not provide GPS, which is necessary for NSDK VPS. " +
                    "Please enable the Spoof Location feature in the NSDK Settings menu in " +
                    "order to try out the VPS Localization Sample."
                );
                _debugDisplayText.text =
                    "Error: Spoof Location is not enabled. The Meta Quest does not provide GPS which NSDK VPS requires. Please enable Location Spoofing in NSDK Settings.";

                Destroy(this);
                return;
            }

            // Initialize the UI elements
            _debugDisplayText.text = string.Empty;
            _arLocationUI.SetActive(!_arLocationManager.AutoTrack);
            _localizeButton.GetComponentInChildren<Text>().text =
                _arLocationManager.AutoTrack ? "Automatic Localization" : "Start Localization";

            if (!_arLocationManager.AutoTrack)
            {
                CreateARLocationMenu();
            }

            // Start the location service
            if (Input.location.status == LocationServiceStatus.Stopped)
            {
                UpdateStatusText("Stopped");
                Input.location.Start();
            }

            UpdateGpsText();

            if (_arLocationManager.subsystem != null)
            {
                _arLocationManager.DebuggerDataReceived += OnDebugInfoProvided;
            }
            else
            {
                Debug.LogError("No XRVps2Subsystem found.");
                _debugDisplayText.text =
                    "Error: No XRVps2Subsystem found.";
            }
        }

        private void OnDestroy()
        {
            if (_arLocationManager.subsystem != null)
            {
                _arLocationManager.DebuggerDataReceived -= OnDebugInfoProvided;
            }
        }

        private void CreateARLocationMenu()
        {
            var arLocations = _arLocationManager.ARLocations;
            foreach (var arLocation in arLocations)
            {
                _arLocationDropdown.options.Add(new Dropdown.OptionData(arLocation.name));
                _arLocationsDropdownItems.Add(arLocation);
            }

            if (_arLocationsDropdownItems.Count > 0)
            {
                _localizeButton.interactable = true;
            }
        }

        private void UpdateGpsText()
        {
            // First, check if user has location service enabled
            if (!Input.location.isEnabledByUser)
            {
                _gpsDisplayText.text = "Location service is not enabled";
                return;
            }

            var latitude = Input.location.lastData.latitude;
            var longitude = Input.location.lastData.longitude;
            var altitude = Input.location.lastData.altitude;

            _gpsDisplayText.text = $"Latitude: {latitude}\nLongitude: {longitude}\nAltitude: {altitude}";
        }

        private void UpdateStatusText(string status)
        {
            _localizationStatusDisplayText.text = "Status: " + status;
        }

        private void LocalizeButton_OnClick()
        {
            if (_arLocationManager.AutoTrack)
            {
                return;
            }

            UpdateGpsText();
            UpdateStatusText(!_localizationStatus ? "Trying to localize..." : "Stopped");
            _localizeButton.GetComponentInChildren<Text>().text =
                !_localizationStatus ? "Stop Localization" : "Start Localization";

            if (!_localizationStatus)
            {
                var currentIndex = _arLocationDropdown.value;
                var arLocation = _arLocationsDropdownItems[currentIndex];
                Debug.Log($"Location: {arLocation.name}");
              //  Debug.Log($"Payload length: {arLocation.Payload?.Length}");
              //  Debug.Log($"Latitude: {arLocation.Latitude}");
              //  Debug.Log($"Longitude: {arLocation.Longitude}");
                _arLocationManager.SetARLocations(arLocation);
                Debug.Log("StartARLocationTracking called");
                Debug.Log($"Subsystem = {_arLocationManager.subsystem != null}");
                Debug.Log($"Subsystem running = {_arLocationManager.subsystem?.running}");
                _arLocationManager.StartARLocationTracking();
                Debug.Log("Tracking started");
                Debug.Log($"Subsystem = {_arLocationManager.subsystem}");
                Debug.Log($"Subsystem running = {_arLocationManager.subsystem?.running}");
                Debug.Log($"AutoTrack = {_arLocationManager.AutoTrack}");
                _localizationStatus = true;
            }
            else
            {
                _arLocationManager.StopARLocationTracking();
                _localizationStatus = false;
            }
        }

        private void OnAnchorChanged(ARTrackablesChangedEventArgs<ARVps2Anchor> changes)
        {
            foreach (var anchor in changes.updated)
            {
                if (_arLocationManager.AnchorToARLocationMap.TryGetValue(anchor, out var arLocation))
                {
                    arLocation.gameObject.SetActive(anchor.trackingState == TrackingState.Tracking);
                    UpdateStatusText(
                        $"{(anchor.trackingState == TrackingState.Tracking ? "Tracking Success" : $"Tracking Failure (reason: {anchor.trackingState})")}");
                }
            }
        }

        private void OnDebugInfoProvided(VpsDebuggerDataEvent info)
        {
            if (info.NetworkRequestState?.Status == NetworkRequestStatus.Failed)
            {
                _debugDisplayText.text = "Network error: " + info.NetworkRequestState +
                    "\nPlease ensure you are connected to WIFI and successfully logged in with a Niantic Spatial account. .";
            }
        }
    }
}
