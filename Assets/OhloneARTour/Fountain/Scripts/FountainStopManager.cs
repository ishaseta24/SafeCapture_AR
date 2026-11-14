using NianticSpatial.NSDK.AR.VPS2;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class FountainStopManager : MonoBehaviour
{
    [Header("VPS Anchor Root")]
    [SerializeField] private Transform _vpsAnchorRoot;

    [Header("Hut Spawning")]
    [SerializeField] private GameObject _hutPrefab;
    [SerializeField] private Transform[] _hutSpawnPoints;

    [Header("Audio")]
    [SerializeField] private AudioSource _soundscape;
    [SerializeField] private AudioSource _narration;
    [SerializeField] private AudioSource _fire;

    private bool _contentSpawned;
    private ARVps2Manager _arVps2Manager;

    private void Start()
    {
        Debug.Log("[STAGE 5] FountainStopManager.Start — scene loaded, searching for ARVps2Manager...");

        if (_vpsAnchorRoot != null) _vpsAnchorRoot.gameObject.SetActive(false);

        _arVps2Manager = FindFirstObjectByType<ARVps2Manager>();
        if (_arVps2Manager != null)
        {
            Debug.Log("[STAGE 5] ✅ ARVps2Manager found — subscribed to trackablesChanged. Waiting for localization...");
            _arVps2Manager.trackablesChanged.AddListener(OnVps2AnchorChanged);
        }
        else
            Debug.LogError("[STAGE 5] ❌ ARVps2Manager not found — is OhloneHome loaded with ARVps2Manager in scene?");
    }

    private void OnDestroy()
    {
        if (_arVps2Manager != null)
            _arVps2Manager.trackablesChanged.RemoveListener(OnVps2AnchorChanged);
    }

    private void OnVps2AnchorChanged(ARTrackablesChangedEventArgs<ARVps2Anchor> changes)
    {
        if (_contentSpawned) return;
        Debug.Log($"[STAGE 6] trackablesChanged — updated={changes.updated.Count}");
        foreach (var anchor in changes.updated)
        {
            Debug.Log($"[STAGE 6] Anchor trackingState={anchor.trackingState}");
            if (anchor.trackingState != TrackingState.Tracking) continue;
            OnTrackingAchieved(anchor.transform);
            return;
        }
    }

    private void OnTrackingAchieved(Transform anchorTransform)
    {
        _contentSpawned = true;
        Debug.Log("[STAGE 7] ✅ Localization SUCCESS — activating AR content.");

        if (_vpsAnchorRoot != null)
        {
            _vpsAnchorRoot.SetParent(anchorTransform, worldPositionStays: false);
            _vpsAnchorRoot.gameObject.SetActive(true);
        }

        SpawnHuts();
        StartAudio();
    }

    private void SpawnHuts()
    {
        if (_hutPrefab == null || _hutSpawnPoints == null) return;
        foreach (var spawnPoint in _hutSpawnPoints)
            Instantiate(_hutPrefab, spawnPoint.position, spawnPoint.rotation);
    }

    private void StartAudio()
    {
        if (_soundscape != null && !_soundscape.isPlaying) _soundscape.Play();
        if (_fire != null && !_fire.isPlaying) _fire.Play();
    }
}
