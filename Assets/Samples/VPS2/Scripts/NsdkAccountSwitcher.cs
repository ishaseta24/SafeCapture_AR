using NianticSpatial.NSDK.AR.Loader;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Switches the active NSDK account at runtime between a dev/local account and a production account.
/// Attach to a GameObject in the home/start UI scene. Assign an NsdkCredentials asset (gitignored)
/// in the Inspector — never paste tokens directly into serialized fields in a scene.
/// The switch takes effect on the next NSDK API call (site query, localization, etc.).
/// </summary>
public class NsdkAccountSwitcher : MonoBehaviour
{
    [Header("Credentials (gitignored ScriptableObject — do NOT use scene fields for tokens)")]
    [SerializeField] private NsdkCredentials _credentials;

    [Header("UI References")]
    [SerializeField] private Button _devButton;
    [SerializeField] private Button _prodButton;
    [SerializeField] private TMP_Text _statusText;

    private static readonly Color ActiveColor = new Color(0.2f, 0.8f, 0.3f);
    private static readonly Color InactiveColor = Color.white;

    private Button _activeButton;

    private void Start()
    {
        if (_credentials == null)
        {
            Debug.LogError("[NsdkAccountSwitcher] No NsdkCredentials asset assigned. " +
                           "Create one via Assets > Create > Ohlone AR > NSDK Credentials and assign it in the Inspector.");
            return;
        }

        if (_devButton != null)
        {
            SetButtonLabel(_devButton, _credentials.devLabel);
            _devButton.onClick.AddListener(SwitchToDev);
        }

        if (_prodButton != null)
        {
            SetButtonLabel(_prodButton, _credentials.prodLabel);
            _prodButton.onClick.AddListener(SwitchToProd);
        }

        // Default to production on start
        SwitchToProd();
    }

    private void SwitchToDev()
    {
        if (_credentials == null) return;
        ApplyAccount(_credentials.devAccessToken, _credentials.devAccessExpiresAt, _credentials.devLabel, _devButton);
    }

    private void SwitchToProd()
    {
        if (_credentials == null) return;
        ApplyAccount(_credentials.prodAccessToken, _credentials.prodAccessExpiresAt, _credentials.prodLabel, _prodButton);
    }

    private void ApplyAccount(string token, int expiresAt, string label, Button button)
    {
        var settings = NsdkSettingsHelper.ActiveSettings;
        if (settings == null)
        {
            Debug.LogError("[NsdkAccountSwitcher] NsdkSettingsHelper.ActiveSettings is null — NSDK not initialized yet.");
            return;
        }

        if (string.IsNullOrEmpty(token))
        {
            Debug.LogWarning($"[NsdkAccountSwitcher] Token for '{label}' is empty. Skipping switch.");
            return;
        }

        settings.UpdateAccess(token, expiresAt, string.Empty, 0);

        if (_activeButton != null)
            SetButtonColor(_activeButton, InactiveColor);

        _activeButton = button;

        if (_activeButton != null)
            SetButtonColor(_activeButton, ActiveColor);

        if (_statusText != null)
            _statusText.text = $"Account: {label}";

        Debug.Log($"[NsdkAccountSwitcher] Switched to '{label}'. Token length: {token.Length}, expiresAt: {expiresAt}");
    }

    private static void SetButtonLabel(Button btn, string label)
    {
        var text = btn.GetComponentInChildren<TMP_Text>();
        if (text != null)
            text.text = label;
    }

    private static void SetButtonColor(Button btn, Color color)
    {
        var image = btn.GetComponent<Image>();
        if (image != null)
            image.color = color;
    }
}
