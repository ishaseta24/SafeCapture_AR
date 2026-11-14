// Copyright 2022-2026 Niantic Spatial.

using System;
using System.Threading.Tasks;
using Niantic.Lightship.AR.Protobuf;
using NianticSpatial.NSDK.AR.Subsystems;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.XRSubsystems;
using UnityEngine;
using UnityEngine.UI;

using Input = NianticSpatial.NSDK.AR.Input;
using NetworkRequestStatus = Niantic.Lightship.AR.Protobuf.NetworkRequestStatus;
using TrackingState = UnityEngine.XR.ARSubsystems.TrackingState;

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    public class VPS2LocalizationSample : MonoBehaviour
    {
        [SerializeField, Tooltip("VPS2 manager")]
        private ARVps2Manager _arVps2Manager;

        [SerializeField, Tooltip("Location mesh manager")]
        private LocationMeshManager _locationMeshManager;
        
        [SerializeField] 
        private GameObject _anchorMarkerPrefab;

        [Header("UI")]
        [SerializeField]
        private Button _stopLocalization;

        [SerializeField]
        private GameObject _arLocationUI;

        [SerializeField, Tooltip("Text to display the VPS2 localization status")]
        private Text _localizationStatusDisplayText;

        [SerializeField]
        private Text _debugDisplayText;

        [Header("Mesh Visibility")]
        [SerializeField]
        private Button _showRefinedMarkerButton;

        private ARVps2Anchor _anchor;
        private bool _isAnchorSet;
        private bool _localizationActive;
        private bool _idleStoppedByUser;
        private bool _idleAnchorSetupFailed;
        private bool _didDownloadMesh;
        private bool _showRefinedMarker = true;
        private Text _showRefinedMarkerButtonText;

        private GameObject _refinedMarker;
        private GameObject _coarseMarker;
        private TrackingState _lastAnchorState = (TrackingState)(-1);
        private Vps2TrackingState? _lastVpsState;
        private string _lastAnchorReason;

        private void Awake()
        {
            if (_arVps2Manager != null)
            {
                _arVps2Manager.UniversalLocalizationEnabled = false;
                _arVps2Manager.VpsMapLocalizationEnabled = true;
                _arVps2Manager.DeviceMapLocalizationEnabled = false;

                Debug.Log(
                    $"VPS2 config: Universal={_arVps2Manager.UniversalLocalizationEnabled}, " +
                    $"VpsMap={_arVps2Manager.VpsMapLocalizationEnabled}, " +
                    $"DeviceMap={_arVps2Manager.DeviceMapLocalizationEnabled}");
            }
        }

        /// <summary>Raised after localization is stopped and anchor state is cleared.</summary>
        public event Action LocalizationStopped;

        private void OnEnable()
        {
            _stopLocalization.onClick.AddListener(LocalizeButton_OnClick);
            _arVps2Manager.DebuggerDataReceived += OnDebugInfoProvided;
            _arVps2Manager.LocalizationRequestRecordAdded += OnLocalizationRequestRecordAdded;
            _showRefinedMarkerButton?.onClick.AddListener(ShowRefinedMarker_OnButtonClick);
        }

        private void OnDisable()
        {
            _stopLocalization.onClick.RemoveListener(LocalizeButton_OnClick);
            _arVps2Manager.DebuggerDataReceived -= OnDebugInfoProvided;
            _arVps2Manager.LocalizationRequestRecordAdded -= OnLocalizationRequestRecordAdded;
            _showRefinedMarkerButton?.onClick.RemoveListener(ShowRefinedMarker_OnButtonClick);
        }
        private void OnDestroy()
        {
            if (_refinedMarker != null)
            {
                Destroy(_refinedMarker);
            }

            if (_coarseMarker != null)
            {
                Destroy(_coarseMarker);
            }
        }

        private void Start()
        {
            _debugDisplayText.text = string.Empty;

            _arLocationUI.SetActive(_arVps2Manager.enabled);

            var localizeButtonLabel = _stopLocalization.GetComponentInChildren<Text>();
            if (localizeButtonLabel != null)
            {
                localizeButtonLabel.text = "Stop Localization";
            }

            _stopLocalization.interactable = false;

            if (_showRefinedMarkerButton != null)
            {
                _showRefinedMarkerButtonText = _showRefinedMarkerButton.GetComponentInChildren<Text>();
                if (_showRefinedMarkerButtonText != null)
                    _showRefinedMarkerButtonText.text = "Hide Mesh";
            }

            if (Input.location.status == LocationServiceStatus.Stopped)
            {
                Input.location.Start();
            }
        }

        private void Update()
        {
            if (!_localizationActive || !_isAnchorSet || _anchor == null)
                return;

            if (_lastAnchorState != _anchor.trackingState ||
                _lastAnchorReason != _anchor.trackingStateReason.ToString())
            {
                _lastAnchorState = _anchor.trackingState;
                _lastAnchorReason = _anchor.trackingStateReason.ToString();

                Debug.Log(
                    $"ANCHOR STATE CHANGED: {_anchor.trackingState}, " +
                    $"reason: {_anchor.trackingStateReason}");
            }

            _localizationStatusDisplayText.text =
                $"Anchor: {_anchor.trackingState}\n" +
                $"Reason: {_anchor.trackingStateReason}";

            ToggleAnchorMarker(_anchor.trackingState);
        }

        private async Task InstantiateAnchorMeshAsync(string _anchorPayload)
        {
            // In this sample we only attempt to download the mesh once
            if (_didDownloadMesh || !_isAnchorSet)
            {
                return;
            }

            if (string.IsNullOrEmpty(_anchorPayload))
            {
                Debug.LogError("Tried to download anchor mesh without a payload.");
                return;
            }

            GameObject mesh;
            try
            {
                mesh = await _locationMeshManager.GetLocationMeshForPayloadAsync(_anchorPayload, 0, false, true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (mesh == null)
            {
                if (!_isAnchorSet || _anchor == null)
                {
                    return;
                }

                Debug.LogError("Mesh download failed");
                return;
            }

            if (!_isAnchorSet || _anchor == null)
            {
                Destroy(mesh);
                return;
            }

            // Assign the mesh
            _refinedMarker = mesh;
            _refinedMarker.transform.SetParent(_anchor.transform, false);
            _refinedMarker.SetActive(false);
            _didDownloadMesh = true;
        }
        
        private void ToggleAnchorMarker(TrackingState trackingState)
        {
            if (_refinedMarker != null)
                _refinedMarker.gameObject.SetActive(
                    trackingState == TrackingState.Tracking && _showRefinedMarker);
            if (_coarseMarker != null)
                _coarseMarker.gameObject.SetActive(trackingState != TrackingState.Tracking ||
                                                (trackingState == TrackingState.Tracking && _refinedMarker == null));
        }

        private void ShowRefinedMarker_OnButtonClick()
        {
            _showRefinedMarker = !_showRefinedMarker;
            if (_showRefinedMarkerButtonText != null)
                _showRefinedMarkerButtonText.text = _showRefinedMarker ? "Hide Mesh" : "Show Mesh";
        }

        /// <summary>
        /// Starts tracking the VPS anchor identified by <paramref name="anchorId"/> (the anchor payload string from site assets).
        /// </summary>
        public bool SetAnchorId(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId))
            {
                _debugDisplayText.text = "Anchor id / payload is empty.";
                return false;
            }

            _idleStoppedByUser = false;
            _idleAnchorSetupFailed = false;

            if (_localizationActive)
            {
                StopTracking(notifyStopped: false);
            }

                Debug.Log($"Anchor payload present: {!string.IsNullOrWhiteSpace(anchorId)}");
                Debug.Log($"Anchor payload length: {anchorId?.Length ?? 0}");
                Debug.Log($"ARVps2Manager assigned: {_arVps2Manager != null}");
                Debug.Log($"ARVps2Manager enabled: {_arVps2Manager != null && _arVps2Manager.isActiveAndEnabled}");
                Debug.Log($"VPS2 subsystem exists: {_arVps2Manager?.subsystem != null}");
                Debug.Log($"Location service status: {Input.location.status}");
                Debug.Log($"Location enabled by user: {Input.location.isEnabledByUser}");

                var location = Input.location.lastData;
                Debug.Log(
                    $"Quest location: lat={location.latitude}, " +
                    $"lon={location.longitude}, " +
                    $"accuracy={location.horizontalAccuracy}, " +
                    $"timestamp={location.timestamp}");

            // Do not enable mesh manager here; mesh download is optional and
            // can interfere with core localization flow. Removed per guidance.

            if (_arVps2Manager.subsystem == null)
            {
                _debugDisplayText.text = "Error: No XRVps2Subsystem found.";
                return false;
            }

            _isAnchorSet = _arVps2Manager.TryTrackAnchor(anchorId, out _anchor);

            Debug.Log($"TryTrackAnchor: {_isAnchorSet}");
            Debug.Log($"Anchor created: {_anchor != null}");
            Debug.Log($"Payload length: {anchorId?.Length ?? 0}");

            if (_anchor != null)
            {
                Debug.Log($"Trackable ID: {_anchor.trackableId}");
                Debug.Log($"Anchor state: {_anchor.trackingState}");
                Debug.Log($"Anchor reason: {_anchor.trackingStateReason}");
            }
            if (!_isAnchorSet)
            {
                Debug.LogWarning("VPSLocalizationSample: TryTrackAnchor failed.");
                _debugDisplayText.text = "Failed to start anchor tracking.";
                _idleAnchorSetupFailed = true;
                return false;
            }

            // Instantiate the debug anchor visualization
            if (_anchorMarkerPrefab != null)
            {
                _coarseMarker = Instantiate(_anchorMarkerPrefab, _anchor.transform, true);
                _coarseMarker.transform.localPosition = Vector3.zero;
                _coarseMarker.transform.localRotation = Quaternion.identity;
                _coarseMarker.transform.localScale = Vector3.one * 0.25f;
            }
            // Download and show the scan mesh so content positions can be calibrated against the real geometry.
            // Disable this call (comment it out) before shipping to end users.
            _ = InstantiateAnchorMeshAsync(anchorId);

            _localizationActive = true;
            _stopLocalization.interactable = true;
            var btnLabel = _stopLocalization.GetComponentInChildren<Text>();
            if (btnLabel != null)
            {
                btnLabel.text = "Stop Localization";
            }

            return true;
        }

        private string BuildLocalizationStatusText()
        {
            var vpsLine = Vps2TrackingStateLine();

            if (!_localizationActive || !_isAnchorSet || _anchor == null)
            {
                var idleHead = _idleAnchorSetupFailed
                    ? "TryTrackAnchor failed"
                    : _idleStoppedByUser
                        ? "Stopped"
                        : "Ready";
                return $"Status: {idleHead}\n{vpsLine}\nAnchor: none";
            }

            var anchorLine = _anchor.trackingState switch
            {
                TrackingState.None => $"Anchor: NONE (reason: {_anchor.trackingStateReason})",
                TrackingState.Limited => $"Anchor: LIMITED (reason: {_anchor.trackingStateReason})",
                TrackingState.Tracking => "Anchor: TRACKING",
                _ => $"Anchor: {_anchor.trackingState}"
            };

            var geo = _anchor.geolocation;
            var geoLine = geo.HasValue && _anchor.trackingState == TrackingState.Tracking
                ? $"Anchor geo: {geo.Value.Latitude:F5}, {geo.Value.Longitude:F5}"
                : "Anchor geo: N/A";

            return $"{vpsLine}\n{anchorLine}\n{geoLine}";
        }

        private string Vps2TrackingStateLine()
        {
            if (_arVps2Manager.TryGetLatestLocalization(out var localization))
            {
                return localization.TrackingState switch
                {
                    Vps2TrackingState.Unavailable => "VPS2: NOT TRACKING",
                    Vps2TrackingState.Coarse => "VPS2: COARSE",
                    Vps2TrackingState.Precise => "VPS2: PRECISE ",
                    _ => "VPS2: UNKNOWN"
                };
            }

            return "VPS2: UNAVAILABLE";
        }

        private void OnLocalizationRequestRecordAdded(
            XRVps2LocalizationRequestRecord record)
        {
            var duration = record.EndTimeMs >= record.StartTimeMs
                ? record.EndTimeMs - record.StartTimeMs
                : 0UL;
            Debug.Log(
                $"REQUEST: " +
                $"Status={record.Status} " +
                $"Error={record.ErrorCode} " +
                $"Duration={duration}");
            Debug.Log(
                $"VPS REQUEST RECORD\n" +
                $"Status: {record.Status}\n" +
                $"{record}");
        }

        private void LocalizeButton_OnClick()
        {
            if (!_localizationActive)
            {
                return;
            }

            StopTracking(notifyStopped: true);
        }

        private void StopTracking(bool notifyStopped)
        {
            if (_locationMeshManager != null)
            {
                _locationMeshManager.enabled = false;
            }

            if (_isAnchorSet && _anchor != null)
            {
                _arVps2Manager.RemoveAnchor(_anchor);
            }

            if (_refinedMarker != null)
            { 
                Destroy(_refinedMarker);
                _refinedMarker = null;
            }
            if (_coarseMarker != null)
            {
                Destroy(_coarseMarker);
                _coarseMarker = null;
            }

            _didDownloadMesh = false;

            _anchor = null;
            _isAnchorSet = false;
            _localizationActive = false;
            _stopLocalization.interactable = false;

            if (notifyStopped)
            {
                _idleStoppedByUser = true;
                LocalizationStopped?.Invoke();
            }
        }

        private void OnDebugInfoProvided(VpsDebuggerDataEvent info)
        {
            Debug.Log($"VPS DEBUG DATA:\n{info}");

            if (info.NetworkRequestState != null)
            {
                Debug.Log(
                    $"Network request status: {info.NetworkRequestState.Status}\n" +
                    $"{info.NetworkRequestState}");
            }

            if (info.NetworkRequestState?.Status == NetworkRequestStatus.Failed)
            {
                _debugDisplayText.text = "Network error: " + info.NetworkRequestState +
                    "\nPlease ensure you are connected to WIFI and successfully logged in with a Niantic Spatial account.";
            }
        }
    }
}
