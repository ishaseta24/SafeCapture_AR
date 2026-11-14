using System.Linq;
using NianticSpatial.NSDK.AR.SceneSegmentation;
using NianticSpatial.NSDK.AR.Subsystems.SceneSegmentation;
using NianticSpatial.NSDK.AR.Utilities;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;
using Utilities;

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    public sealed class SceneSegmentationImageDisplay : MonoBehaviour
    {
        /// <summary>
        /// The shader property ID for the display matrix.
        /// </summary>
        private static readonly int s_displayMatrix = Shader.PropertyToID("_DisplayMatrix");

        [SerializeField]
        private ARSceneSegmentationManager _sceneSegmentationManager;

        [SerializeField]
        private RawImage _rawImage;

        [SerializeField]
        private Material _material;

        [SerializeField]
        private Text _imageInfoText;

        [SerializeField]
        private Dropdown _channelDropdown;

        // The currently selected semantic channel
        private SceneSegmentationChannel _sceneSegmentationChannel = SceneSegmentationChannel.Sky;

        private void OnEnable()
        {
            _sceneSegmentationManager.MetadataInitialized += OnSceneSegmentationMetadataInitialized;
            _channelDropdown.onValueChanged.AddListener(OnChanelDropdownValueChanged);
        }

        private void OnDisable()
        {
            _sceneSegmentationManager.MetadataInitialized -= OnSceneSegmentationMetadataInitialized;
            _channelDropdown.onValueChanged.RemoveListener(OnChanelDropdownValueChanged);
        }

        private void Update()
        {
            // Get the width and height of the viewport
            var rectTransform = _rawImage.rectTransform;
            var viewportWidth = (int)rectTransform.rect.width;
            var viewportHeight = (int)rectTransform.rect.height;

            // Use the XRCameraParams type to describe the viewport to fit the semantics image to
            var viewport = new XRCameraParams
            {
                screenWidth = viewportWidth,
                screenHeight = viewportHeight,
                screenOrientation = XRDisplayContext.GetScreenOrientation()
            };

            // Acquire the texture with the confidence values of the currently selected channel
            var image = _sceneSegmentationManager.GetSceneSegmentationChannelTexture(_sceneSegmentationChannel, out var displayMatrix, viewport);
            if (image != null)
            {
                // Update the texture and material of the RawImage component
                _rawImage.texture = image;
                _rawImage.material = _material;
                _rawImage.material.SetMatrix(s_displayMatrix, displayMatrix);

                // Convert enum to string for display
                string channelName = ARSceneSegmentationManager.GetChannelNameFromEnum(_sceneSegmentationChannel);

                // Log the image information
                _imageInfoText.text = $"{channelName}" + $"\nWidth: {image.width}" + $"\nHeight: {image.height}" +
                    $"\nFormat: {image.format}";
            }
            else
            {
                _rawImage.texture = null;
                _imageInfoText.text = "No image available";
            }
        }

        /// <summary>
        /// Callback for when the semantic metadata (e.g. channel list) becomes available.
        /// </summary>
        private void OnSceneSegmentationMetadataInitialized(ARSceneSegmentationModelEventArgs args)
        {
            // Initialize the channel names in the dropdown menu.
            var channels = _sceneSegmentationManager.Channels;
            // Convert channels to strings for UI display
            var channelNames = channels.Select(ARSceneSegmentationManager.GetChannelNameFromEnum).ToList();

            // Display artificial ground by default.
            if (channels.Count > 3)
            {
                _sceneSegmentationChannel = channels[3];
            }
            var channelName = channelNames[3];

            if (_channelDropdown is not null)
            {
                _channelDropdown.AddOptions(channelNames);

                var dropdownList = _channelDropdown.options.Select(option => option.text).ToList();
                _channelDropdown.value = dropdownList.IndexOf(channelName);
            }
        }

        /// <summary>
        /// Callback for when the semantic channel dropdown UI has a value change.
        /// </summary>
        private void OnChanelDropdownValueChanged(int val)
        {
            // Update the display channel from the dropdown value.
            var channelName = _channelDropdown.options[val].text;
            var channel = _sceneSegmentationManager.GetChannelFromName(channelName);
            if (channel.HasValue)
            {
                _sceneSegmentationChannel = channel.Value;
            }
        }
    }
}
