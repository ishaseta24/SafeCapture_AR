// Copyright 2022-2026 Niantic Spatial.

using System;
using System.Collections;
using System.IO;
using NianticSpatial.NSDK.AR.Mapping;
using UnityEngine;

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    /// <summary>
    /// This class manages creating local maps that are stored to a file on the device
    /// </summary>
    public class Mapper : MonoBehaviour
    {
        [SerializeField]
        private ARDeviceMappingManager _deviceMappingManager;

        //subscribe to this to know mapping has completed
        public Action<bool> _onMappingComplete;

        //if we want the map to be written to the device or not.
        public bool _saveToFile = true;

        void Start()
        {
            //set up on device mapping
            _deviceMappingManager.MappingSplitterMaxDistanceMeters = 10.0f;
            _deviceMappingManager.MappingSplitterMaxDurationSeconds = 1000.0f;

            _deviceMappingManager.enabled = true;
        }

        private Coroutine currentCo;
        private bool _mappingInProgress = false;

        public void RunMappingFor(float seconds)
        {
            _deviceMappingManager.MapFinalized += OnDeviceMapFinalized;
            currentCo = StartCoroutine(RunMapping(seconds));
        }

        public bool GetMap(out byte[] mapData)
        {
            return _deviceMappingManager.TryGetMapData(out mapData);
        }

        private void OnDestroy()
        {
            _deviceMappingManager.MapFinalized -= OnDeviceMapFinalized;
        }

        //scanning is just on a timer to make some of the UX eaiser
        //you can easily modify the code have a start and stop button if you prefer
        private IEnumerator RunMapping(float seconds)
        {
            // Reset the ARDeviceMappingManager
            _deviceMappingManager.enabled = false;
            yield return null;
            _deviceMappingManager.enabled = true;
            yield return null;
            //start mapping
            _mappingInProgress = true;
            _deviceMappingManager.StartMapping();

            //end mapping after a few seconds
            yield return new WaitForSeconds(seconds);
            _deviceMappingManager.StopMapping();
            _mappingInProgress = false;
        }

        //called if you hit exit while scanning is happening.
        public void StopMapping()
        {
            if (_mappingInProgress)
            {
                StopCoroutine(currentCo);
                _deviceMappingManager.MapFinalized -= OnDeviceMapFinalized;
                _deviceMappingManager.StopMapping();
                _mappingInProgress = false;
            }
        }


        public void ClearAllState()
        {
            StopMapping();
            _deviceMappingManager.enabled = false;
            _deviceMappingManager.ClearMap();
        }

        private void OnDeviceMapFinalized(byte[] mapData)
        {
            _deviceMappingManager.MapFinalized -= OnDeviceMapFinalized;

            bool success = false;

            byte[] bytesToSave = null;
            if (_deviceMappingManager.TryGetMapData(out var fullMap) &&
                fullMap != null &&
                fullMap.Length > 0)
            {
                bytesToSave = fullMap;
            }
            else if (mapData != null && mapData.Length > 0)
            {
                bytesToSave = mapData;
            }

            if (bytesToSave != null && bytesToSave.Length > 0)
            {
                success = true;
                if (_saveToFile == true)
                {
                    var fileName = OnDevicePersistence.k_mapFileName;
                    var path = Path.Combine(Application.persistentDataPath, fileName);
                    File.WriteAllBytes(path, bytesToSave);
                }
            }

            _onMappingComplete?.Invoke(success);
        }
    }
}
