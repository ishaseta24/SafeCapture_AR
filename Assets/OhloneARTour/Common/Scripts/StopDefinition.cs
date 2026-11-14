using UnityEngine;

[CreateAssetMenu(fileName = "StopDefinition", menuName = "Ohlone AR Quest/Stop Definition")]
public class StopDefinition : ScriptableObject
{
    [Header("Identity")]
    public string stopName;
    public string sceneName;
    [TextArea] public string description;

    [Header("Location")]
    [Tooltip("GPS coordinates used for VPS spoofed location")]
    public float latitude;
    public float longitude;

    [Header("VPS")]
    [Tooltip("Anchor payload string from Niantic Lightship console")]
    [TextArea] public string anchorPayload;

    [Header("UI")]
    public Sprite thumbnail;
}
