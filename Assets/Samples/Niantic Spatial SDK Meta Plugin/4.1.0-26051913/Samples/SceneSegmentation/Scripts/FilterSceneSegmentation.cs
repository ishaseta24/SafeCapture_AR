// Copyright 2022-2026 Niantic Spatial.

using System.Linq;
using NianticSpatial.NSDK.AR.SceneSegmentation;
using UnityEngine;
using UnityEngine.UI;

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    public class FilterSceneSegmentation : MonoBehaviour
    {
        [SerializeField]
        private ARSceneSegmentationManager _sceneSegmentationManager;

        [SerializeField]
        private NsdkSceneSegmentationOverlay _sceneSegmentationOverlay;

        [SerializeField]
        private Dropdown _channelDropdown;

        // Display ground by default
        private const int DefaultChannel = 3;

        private void OnEnable()
        {
            _sceneSegmentationManager.MetadataInitialized += OnMetadataInitialized;
            _channelDropdown.onValueChanged.AddListener(OnChannelDropdownValueChanged);
        }

        private void OnDisable()
        {
            _sceneSegmentationManager.MetadataInitialized -= OnMetadataInitialized;
            _channelDropdown.onValueChanged.RemoveListener(OnChannelDropdownValueChanged);
        }

        private void OnChannelDropdownValueChanged(int val)
        {
            _sceneSegmentationOverlay.SetChannel(val);
        }

        private void OnMetadataInitialized(ARSceneSegmentationModelEventArgs obj)
        {
            // Initialize the channel names in the dropdown menu.
            var channels = _sceneSegmentationManager.Channels;
            // Convert channels to strings for UI display
            var channelNames = channels.Select(ARSceneSegmentationManager.GetChannelNameFromEnum).ToList();

            // Set the default channel.
            _sceneSegmentationOverlay.SetChannel(DefaultChannel);

            if (_channelDropdown is not null)
            {
                _channelDropdown.AddOptions(channelNames);
                _channelDropdown.value = DefaultChannel;
            }
        }
    }
}
