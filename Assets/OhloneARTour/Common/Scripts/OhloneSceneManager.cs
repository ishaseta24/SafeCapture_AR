using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class OhloneSceneManager : MonoBehaviour
{
    public static OhloneSceneManager Instance { get; private set; }

    [Serializable]
    public struct SiteSceneEntry
    {
        [Tooltip("Site name from Niantic Lightship (must match exactly)")]
        public string siteName;
        [Tooltip("Unity scene name to load additively (must be in Build Settings)")]
        public string sceneName;
    }

    [Tooltip("Map each Lightship site name to its Unity scene")]
    [SerializeField] private List<SiteSceneEntry> _siteSceneMappings = new List<SiteSceneEntry>();

    public string CurrentSiteName { get; private set; }

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

    public string GetSceneForSite(string siteName)
    {
        Debug.Log($"[STAGE 3] OhloneSceneManager — searching {_siteSceneMappings.Count} mapping(s) for '{siteName}'");
        foreach (var entry in _siteSceneMappings)
        {
            if (string.Equals(entry.siteName, siteName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[STAGE 3] ✅ Match found: '{siteName}' → '{entry.sceneName}'");
                return entry.sceneName;
            }
        }
        Debug.LogWarning($"[STAGE 3] ❌ No match for '{siteName}'. Registered keys: [{string.Join(", ", _siteSceneMappings.ConvertAll(e => e.siteName))}]");
        return null;
    }

    public void LoadSite(string sceneName)
    {
        if (!string.IsNullOrEmpty(CurrentSiteName))
        {
            Debug.LogWarning($"[STAGE 4] ❌ Already loaded '{CurrentSiteName}'. Unload first.");
            return;
        }

        Debug.Log($"[STAGE 4] ✅ Loading scene '{sceneName}' additively...");
        CurrentSiteName = sceneName;
        SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
    }

    public void UnloadCurrentSite()
    {
        if (string.IsNullOrEmpty(CurrentSiteName))
        {
            Debug.LogWarning("[OhloneSceneManager] No site currently loaded.");
            return;
        }

        var scene = SceneManager.GetSceneByName(CurrentSiteName);
        if (scene.isLoaded)
        {
            Debug.Log($"[OhloneSceneManager] Unloading site: {CurrentSiteName}");
            SceneManager.UnloadSceneAsync(CurrentSiteName);
        }

        CurrentSiteName = null;
    }
}
