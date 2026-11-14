using UnityEngine;

/// <summary>
/// Stores NSDK API tokens for dev and production accounts.
/// THIS FILE IS GITIGNORED — never commit the .asset to source control.
/// Create your local copy via Assets > Create > Ohlone AR > NSDK Credentials,
/// then drag it into NsdkAccountSwitcher in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "NsdkCredentials", menuName = "Ohlone AR/NSDK Credentials")]
public class NsdkCredentials : ScriptableObject
{
    [Header("Dev / Local Account")]
    public string devLabel = "Dev Account";
    [TextArea(2, 5)] public string devAccessToken;
    public int devAccessExpiresAt;

    [Header("Production Account")]
    public string prodLabel = "Production Account";
    [TextArea(2, 5)] public string prodAccessToken;
    public int prodAccessExpiresAt;
}
