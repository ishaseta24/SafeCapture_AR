using UnityEngine;

public class AppManager : MonoBehaviour
{
    public static AppManager Instance { get; private set; }

    public string SelectedSitePayload { get; private set; }
    public Transform AnchorTransform { get; private set; }

    public event System.Action<Transform> OnTrackingAchievedEvent;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Called from VPS2LocalizeDemo after TryTrackAnchor succeeds — maps site name to scene and loads it additively.
    public void SelectSite(string siteName, string anchorPayload)
    {
        Debug.Log($"[STAGE 3] AppManager.SelectSite — looking up scene for siteName='{siteName}'");
        SelectedSitePayload = anchorPayload;

        if (OhloneSceneManager.Instance == null)
        {
            Debug.LogWarning("[STAGE 3] ❌ OhloneSceneManager not found — not in scene?");
            return;
        }

        var sceneName = OhloneSceneManager.Instance.GetSceneForSite(siteName);
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning($"[STAGE 3] ❌ No scene mapped for '{siteName}' — check OhloneSceneManager mappings in Inspector.");
            return;
        }

        Debug.Log($"[STAGE 3] ✅ Mapped '{siteName}' → '{sceneName}' — requesting load.");
        OhloneSceneManager.Instance.LoadSite(sceneName);
    }

    public void OnTrackingAchieved(Transform anchorTransform)
    {
        AnchorTransform = anchorTransform;
        Debug.Log("[AppManager] Tracking achieved — firing OnTrackingAchievedEvent.");
        OnTrackingAchievedEvent?.Invoke(anchorTransform);
    }

    public void ReturnHome()
    {
        SelectedSitePayload = null;
        AnchorTransform = null;

        if (OhloneSceneManager.Instance != null)
            OhloneSceneManager.Instance.UnloadCurrentSite();
    }

}
