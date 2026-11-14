using UnityEngine;

public class QuestOhloneTestingManager : MonoBehaviour
{
    public static QuestOhloneTestingManager Instance { get; private set; }

    [SerializeField] private bool _isTestMode;

    public bool IsTestMode
    {
        get => _isTestMode;
        set
        {
            if (_isTestMode == value) return;
            _isTestMode = value;
            if (_isTestMode)
                OnTestModeOn?.Invoke();
            else
                OnTestModeOff?.Invoke();
        }
    }

    public event System.Action OnTestModeOn;
    public event System.Action OnTestModeOff;

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

    private void Start()
    {
        if (_isTestMode)
            OnTestModeOn?.Invoke();
    }

    public void SetTestMode(bool value) => IsTestMode = value;
}
