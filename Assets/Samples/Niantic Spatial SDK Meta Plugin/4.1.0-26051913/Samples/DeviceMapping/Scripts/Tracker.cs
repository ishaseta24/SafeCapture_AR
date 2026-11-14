// Copyright 2022-2026 Niantic Spatial

using System;
using System.Collections;
using System.IO;
using NianticSpatial.NSDK.AR.Mapping;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.XRSubsystems;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    /// <summary>
    /// This class handles localising to a map
    /// It loads the anchor from a file on device
    /// An has helpers functions for placing things relative to the maps anchor
    /// </summary>
    public class Tracker : MonoBehaviour
    {
        [SerializeField]
        private ARVps2Manager _vps2Manager;

        private ARVps2Anchor _anchor;
        private DeviceMapAccessController _deviceMapAccessController;

        //do you want to load from a file or not.
        public bool _loadFromFile = true;

        //subscribe to this to know when tracking has beed successful
        public Action<bool> _tracking;

        //map data
        private byte[] _mapData;

        //adding a handle in case we try to add items to the scene before localiseation is finished
        //this could happen while using the data store.
        GameObject _tempAnchor;

        public Transform Anchor
        {
            get
            {
                //if we are localised return the anchor
                if (_anchor)
                    return _anchor.transform;
                else
                {
                    //if we are not yet localised return a temp root.
                    if (!_tempAnchor)
                    {
                        _tempAnchor = new GameObject("TempAnchor");
                    }

                    return _tempAnchor.transform;
                }
            }
        }

        private void Update()
        {
            //cleans up any items that were added before we localised by reparenting them to the proper anchor
            if (_anchor && _anchor.trackingState == TrackingState.Tracking)
            {
                if (_tempAnchor)
                {
                    //move them
                    for (int i = 0; i < _tempAnchor.transform.childCount; i++)
                    {
                        _tempAnchor.transform.GetChild(i).SetParent(_anchor.transform, false);
                    }
                }
            }
        }

        private void Start()
        {
            _deviceMapAccessController = DeviceMapAccessController.Acquire();
            _vps2Manager.DeviceMapLocalizationEnabled = true;
            _vps2Manager.VpsMapLocalizationEnabled = false;
            _vps2Manager.GeolocationSmoothingEnabled = true;
            _vps2Manager.DeviceMapLocalizationFramerate = 10;
        }

        private void OnDestroy()
        {
            _deviceMapAccessController.Release();
            _deviceMapAccessController = null;
        }

        private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARVps2Anchor> args)
        {
            if (args.updated.Count > 0 && args.updated[0].trackingState == TrackingState.Tracking)
            {
                _tracking?.Invoke(true);
            }
        }

        //tracking should remain on until you no
        public void StartTracking()
        {
            _vps2Manager.trackablesChanged.AddListener(OnTrackablesChanged);

            if (_loadFromFile)
            {
                // Read a new device map from file
                var fileName = OnDevicePersistence.k_mapFileName;
                var path = Path.Combine(Application.persistentDataPath, fileName);
                _mapData = File.ReadAllBytes(path);
            }

            StartCoroutine(RestartTrackingDataStore());
        }

        public void StopAndDestroyAnchor()
        {
            _vps2Manager.trackablesChanged.RemoveListener(OnTrackablesChanged);

            _vps2Manager.enabled = false;

            _anchor = null;
            _mapData = null;
        }

        public void ClearAllState()
        {
            StopAndDestroyAnchor();
            _deviceMapAccessController.ClearDeviceMaps();
        }

        public void LoadMap(byte[] serializedDeviceMap)
        {
            _mapData = serializedDeviceMap;
        }

        private IEnumerator RestartTrackingDataStore()
        {
            //this needs to be set!
            while (_mapData == null || _mapData.Length == 0)
                yield return new WaitForSeconds(1);


            // Replace any in-session store contents so the file is the single source of truth.
            _deviceMapAccessController.ClearDeviceMaps();

            // Add the map data to the storage
            if (!_deviceMapAccessController.AddMap(_mapData))
            {
                Debug.LogError("Tracker: Failed to add map data to storage");
                yield break;
            }

            // Create root anchor from the map data
            if (!_deviceMapAccessController.CreateRootAnchor(out var rootAnchorPayload))
            {
                Debug.LogError("Tracker: Failed to create root anchor");
                yield break;
            }

            _vps2Manager.enabled = false;

            // start tracking after stop tracking needs "some" time in between...
            yield return null;

            _vps2Manager.enabled = true;

            // Set up a new tracking with a new anchor using the base64 string payload
            Debug.Log($"[Tracker] Payload length = {rootAnchorPayload?.Length}");
            var started = _vps2Manager.TryTrackAnchor(
                rootAnchorPayload,
                out _anchor);
            Debug.Log($"[Tracker] TryTrackAnchor = {started}");
            Debug.Log($"[Tracker] Anchor null = {_anchor == null}");
            if (_anchor != null)
            {
                Debug.Log($"[Tracker] TrackableId = {_anchor.trackableId}");
            }
        }

        //convert a world point to an anchor relative one.
        public Vector3 GetAnchorRelativePosition(Vector3 pos)
        {
            return _anchor.transform.InverseTransformPoint(pos);
        }

        //parent the game object under the anchor.
        public void AddObjectToAnchor(GameObject go)
        {
            go.transform.SetParent(Anchor.transform);
        }
    }
}
