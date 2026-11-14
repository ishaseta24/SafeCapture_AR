// Copyright 2022-2026 Niantic Spatial.

using System.Collections.Generic;
using NianticSpatial.NSDK.AR;
using NianticSpatial.NSDK.AR.SceneSegmentation;
using NianticSpatial.NSDK.AR.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.XR.Interaction.Toolkit.Interactors;
#else
using UnityEngine.XR.Interaction.Toolkit;
#endif

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    public class RaycastSceneSegmentation : MonoBehaviour
    {
        [SerializeField]
        private ARSceneSegmentationManager _sceneSegmentationManager;

        [SerializeField]
        private NsdkSceneSegmentationOverlay _sceneSegmentationOverlay;

        [SerializeField]
        private XRRayInteractor _rayInteractor;

        [SerializeField]
        private Camera _camera;

        [SerializeField, Header("UI Elements")]
        private Text _resultText;

        private void Update()
        {
            // Wait for the semantics overlay to be ready
            if (!_sceneSegmentationOverlay.Intrinsics.HasValue)
            {
                return;
            }

            var intrinsics = _sceneSegmentationOverlay.Intrinsics.Value;
            var resolution = intrinsics.resolution;
            var imageParams = new XRCameraParams
            {
                screenWidth = resolution.x,
                screenHeight = resolution.y,
                screenOrientation = ScreenOrientation.LandscapeLeft
            };

            // Pick image coordinates
            var imageCoordinates = PickImageCoordinates(intrinsics, _sceneSegmentationOverlay.BackProjectionDistance);

            // Query for semantics classification at the image coordinates
            var channels =
                _sceneSegmentationManager.GetChannelNamesAt(imageCoordinates.x, imageCoordinates.y, imageParams);

            // Display results
            SetResultText(channels, imageCoordinates);
        }

        /// <summary>
        /// Updates the UI text with the channels found at the specified screen point.
        /// </summary>
        /// <param name="channelsAtPoint">Semantic classifications found at screen point.</param>
        /// <param name="screenPoint">Currently inspected screen coordinates.</param>
        private void SetResultText(List<string> channelsAtPoint, Vector2Int screenPoint)
        {
            if (channelsAtPoint == null || channelsAtPoint.Count == 0)
            {
                _resultText.text = $"No channels found at {screenPoint.x}, {screenPoint.y}:\n";
                return;
            }

            var resultText = $"Found {channelsAtPoint.Count} Channels at {screenPoint.x}, {screenPoint.y}:\n";
            foreach (var result in channelsAtPoint)
            {
                resultText += result + "\n";
            }

            _resultText.text = resultText;
        }

        /// <summary>
        /// Raycasts the image plane and returns the image coordinates at the intersection.
        /// </summary>
        /// <param name="intrinsics">The intrinsic parameters for the image we are raycasting.</param>
        /// <param name="planeDistance">The distance of the image plane from the Unity camera.</param>
        /// <returns>Pixel positions at the ray insersection. Default when the intersection is out of boundaries..</returns>
        private Vector2Int PickImageCoordinates(XRCameraIntrinsics intrinsics, float planeDistance)
        {
            // Get the ray from the mouse/controller
            var ray = GetRayFromInput();

            // Construct the image plane at the back projection distance
            var plane = new Plane(
                inNormal: -_camera.transform.forward,
                inPoint: _camera.transform.position + _camera.transform.forward * planeDistance);

            // Intersect the image plane from the camera
            if (!plane.Raycast(ray, out var distance))
            {
                return default;
            }

            // Get the point of intersection
            var worldPoint = ray.GetPoint(distance);

            // Convert the world point to image coordinates
            return WorldPointToImageCoordinates(worldPoint, intrinsics);
        }

        /// <summary>
        /// On device, this will return a ray from the controller.
        /// In the editor, it will return a ray from the camera.
        /// </summary>
        /// <returns></returns>
        private Ray GetRayFromInput()
        {
#if !UNITY_EDITOR
            // Construct a ray from the controller
            _rayInteractor.GetLineOriginAndDirection(out var origin, out var direction);
            Ray ray = new Ray(origin, direction);
#else
            // In the editor, use the mouse position
#if ENABLE_INPUT_SYSTEM
            var screenPoint = Mouse.current.position.ReadValue();
#else
            var screenPoint = Input.mousePosition;
#endif
            Ray ray = _camera.ScreenPointToRay(screenPoint);
#endif
            return ray;
        }

        /// <summary>
        /// Project the specified world-space point onto the image.
        /// </summary>
        private Vector2Int WorldPointToImageCoordinates(Vector3 worldPoint, XRCameraIntrinsics intrinsics)
        {
            // Convert to camera space
            var cameraToWorld = InputReader.CurrentPose ?? Matrix4x4.identity;
            var worldToCamera = cameraToWorld.inverse;
            Vector3 cameraSpacePoint = worldToCamera.MultiplyPoint(worldPoint);

            // Project to image coordinates
            int px = Mathf.FloorToInt(intrinsics.focalLength.x * cameraSpacePoint.x / cameraSpacePoint.z +
                intrinsics.principalPoint.x);
            int py = Mathf.FloorToInt(intrinsics.focalLength.y * cameraSpacePoint.y / cameraSpacePoint.z +
                intrinsics.principalPoint.y);

            // Clamp to image bounds
            return new Vector2Int(Mathf.Clamp(px, 0, intrinsics.resolution.x),
                Mathf.Clamp(py, 0, intrinsics.resolution.y));
        }
    }
}
