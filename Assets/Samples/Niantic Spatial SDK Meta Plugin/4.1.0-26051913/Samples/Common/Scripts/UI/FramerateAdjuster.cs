// Copyright 2022-2026 Niantic Spatial.

using System;
using NianticSpatial.NSDK.AR.Meshing;
using NianticSpatial.NSDK.AR.Occlusion;
using NianticSpatial.NSDK.AR.SceneSegmentation;
using NianticSpatial.NSDK.AR.Subsystems.Occlusion;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    public class FramerateAdjuster : MonoBehaviour
    {
        private const string DisplayFormat = "{0} Target Framerate: {1}";
        private const string DepthLabel = "Depth";
        private const string SceneSegmentationLabel = "SceneSegmentation";
        private const string MeshingLabel = "Meshing";

        [SerializeField]
        private GameObject _framerateSliderCanvasObject;

        [Header("Occlusion")]
        [SerializeField]
        private NsdkOcclusionExtension _occlusionExtension;

        [SerializeField]
        private Slider _depthFramerateSlider;

        [SerializeField]
        private Text _depthFpsText;

        [Header("Scene Segmentation")]
        [SerializeField]
        private ARSceneSegmentationManager _segmentationManager;

        [SerializeField]
        private Slider _segmentationFramerateSlider;

        [SerializeField]
        private Text _segmentationFpsText;

        [Header("Meshing")]
        [SerializeField]
        private NsdkMeshingExtension _meshingExtension;

        [SerializeField]
        private Slider _meshingFramerateSlider;

        [SerializeField]
        private Text _meshingFpsText;

        private bool _isUsingNsdkDepth;

        private void Start()
        {
            // Check if we're using NSDK depth
            var xrManager = XRGeneralSettings.Instance.Manager;
            var occlusionSubsystem = xrManager.activeLoader.GetLoadedSubsystem<XROcclusionSubsystem>();
            _isUsingNsdkDepth = occlusionSubsystem is NsdkOcclusionSubsystem;

            // Set all the framerate sliders to listen to values if they're corresponding extensions are not null
            if (_depthFramerateSlider is not null)
            {
                _depthFramerateSlider.transform.parent.gameObject.SetActive(_occlusionExtension is not null);
                if (_occlusionExtension is not null)
                {
                    _depthFramerateSlider.value = _occlusionExtension.TargetFrameRate;
                    _depthFpsText.text =
                        string.Format(DisplayFormat, DepthLabel, _occlusionExtension.TargetFrameRate);

                    _depthFramerateSlider.onValueChanged.AddListener
                    (
                        value => OnSliderValueChange(DepthLabel, value)
                    );
                }
                else
                {
                    Debug.LogWarning("Occlusion extension is null: cannot adjust depth framerate");
                }
            }

            if (_segmentationFramerateSlider is not null)
            {
                _segmentationFramerateSlider.transform.parent.gameObject.SetActive(_segmentationManager is not null);
                if (_segmentationManager is not null)
                {
                    _segmentationFramerateSlider.value = _segmentationManager.TargetFrameRate;
                    _segmentationFpsText.text =
                        string.Format(DisplayFormat, SceneSegmentationLabel, _segmentationManager.TargetFrameRate);

                    _segmentationFramerateSlider.onValueChanged.AddListener
                    (
                        value =>
                        OnSliderValueChange(SceneSegmentationLabel, value)
                    );
                }
                else
                {
                    Debug.LogWarning("Segmentation manager is null: cannot adjust segmentation framerate");
                }
            }

            if (_meshingFramerateSlider is not null)
            {
                _meshingFramerateSlider.transform.parent.gameObject.SetActive(_meshingExtension is not null);
                if (_meshingExtension is not null)
                {
                    _meshingFramerateSlider.value = _meshingExtension.TargetFrameRate;
                    _meshingFpsText.text =
                        string.Format(DisplayFormat, MeshingLabel, _meshingExtension.TargetFrameRate);

                    _meshingFramerateSlider.onValueChanged.AddListener
                    (
                        value => OnSliderValueChange(MeshingLabel, value)
                    );
                }
                else
                {
                    Debug.LogWarning("Meshing extension is null: cannot adjust meshing framerate");
                }
            }
        }

        public void ToggleFramerateSlider()
        {
            _framerateSliderCanvasObject.SetActive(!_framerateSliderCanvasObject.activeSelf);
        }

        private void OnSliderValueChange(string sliderName, float value)
        {
            switch (sliderName)
            {
                case DepthLabel:
                    _occlusionExtension.TargetFrameRate = (uint)value;
                    _depthFpsText.text =
                        string.Format(DisplayFormat, DepthLabel, _occlusionExtension.TargetFrameRate);

                    if (_meshingExtension is not null && _isUsingNsdkDepth)
                    {
                        // Ensure that the meshing framerate is not higher than the depth framerate
                        _meshingExtension.TargetFrameRate =
                            (int)Math.Min(_meshingExtension.TargetFrameRate, _occlusionExtension.TargetFrameRate);
                        _meshingFpsText.text =
                            string.Format(DisplayFormat, MeshingLabel, _meshingExtension.TargetFrameRate);
                        _meshingFramerateSlider.value = _meshingExtension.TargetFrameRate;
                    }
                    break;

                case SceneSegmentationLabel:
                    _segmentationManager.TargetFrameRate = (uint)value;
                    _segmentationFpsText.text =
                        string.Format(DisplayFormat, SceneSegmentationLabel, _segmentationManager.TargetFrameRate);
                    break;

                case MeshingLabel:
                    // Ensure that the meshing framerate is not higher than the depth framerate for NSDK depth
                    _meshingExtension.TargetFrameRate =
                        _isUsingNsdkDepth
                            ? (int)Math.Min(value, _occlusionExtension.TargetFrameRate)
                            : (int)value;

                    _meshingFpsText.text =
                        string.Format(DisplayFormat, MeshingLabel, _meshingExtension.TargetFrameRate);

                    _meshingFramerateSlider.value = _meshingExtension.TargetFrameRate;
                    break;

                default:
                    Debug.LogWarning("Invalid slider name");
                    break;
            }
        }
    }
}
