// Quest-aware billboard. Targets the XR center-eye camera by tag so it works
// correctly in stereo. Drop-in replacement for the mobile BillboardScript.

using UnityEngine;

public class QuestBillboard : MonoBehaviour
{
    private Transform _cameraTransform;

    private void Start()
    {
        // XR center-eye camera is tagged MainCamera by the Quest XR rig
        var cam = Camera.main;
        if (cam != null)
            _cameraTransform = cam.transform;
        else
            Debug.LogWarning("[QuestBillboard] No MainCamera found. Billboard will not rotate.");
    }

    private void LateUpdate()
    {
        if (_cameraTransform == null) return;
        transform.LookAt(_cameraTransform.position);
        transform.Rotate(0f, 180f, 0f);
    }
}
