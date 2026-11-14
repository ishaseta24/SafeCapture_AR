using UnityEngine;
using UnityEngine.SceneManagement;

public class HomeUIController : MonoBehaviour
{
    [SerializeField] private GameObject _homeUI;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (OhloneSceneManager.Instance == null) return;
        if (scene.name != OhloneSceneManager.Instance.CurrentSiteName) return;
        if (_homeUI != null) _homeUI.SetActive(false);
        Debug.Log("[HomeUIController] Stop scene loaded — hiding home UI.");
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (OhloneSceneManager.Instance == null) return;
        if (_homeUI != null) _homeUI.SetActive(true);
        Debug.Log("[HomeUIController] Stop scene unloaded — showing home UI.");
    }
}
