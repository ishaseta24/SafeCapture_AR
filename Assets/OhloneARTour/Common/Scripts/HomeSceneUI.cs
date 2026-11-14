using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Attach to the Home Scene canvas. Populate _stops in Inspector with StopDefinition assets.
// Instantiates one button per stop; tapping it calls AppManager.SelectStop().
public class HomeSceneUI : MonoBehaviour
{
    [SerializeField] private StopDefinition[] _stops;
    [SerializeField] private GameObject _stopButtonPrefab;
    [SerializeField] private Transform _buttonContainer;

    private void Start()
    {
        if (AppManager.Instance == null)
        {
            Debug.LogWarning("[HomeSceneUI] AppManager not found. Make sure it exists in the scene.");
            return;
        }

        foreach (var stop in _stops)
        {
            if (stop == null) continue;

            var buttonGO = Instantiate(_stopButtonPrefab, _buttonContainer);

            var label = buttonGO.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = stop.stopName;

            var thumbnail = buttonGO.GetComponentInChildren<Image>();
            if (thumbnail != null && stop.thumbnail != null)
                thumbnail.sprite = stop.thumbnail;

            var button = buttonGO.GetComponent<Button>();
            if (button != null)
            {
                var capturedStop = stop;
                button.onClick.AddListener(() => AppManager.Instance.SelectSite(capturedStop.stopName, capturedStop.anchorPayload));
            }
        }
    }
}
