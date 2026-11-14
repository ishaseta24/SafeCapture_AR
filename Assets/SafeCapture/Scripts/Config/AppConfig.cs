using System;
using System.IO;
using UnityEngine;

[Serializable]
public class AppConfig
{
    public string   relayUrl          = "";
    public string   relayToken        = "";
    public float    detectIntervalSec = 3f;
    public int      maxImageWidth     = 960;
    public string[] prompts           = new string[0];

    private static AppConfig _instance;
    public  static AppConfig Instance => _instance ??= Load();

    // relayToken empty means unconfigured — vision is disabled (fails safe)
    public bool IsConfigured => !string.IsNullOrEmpty(relayUrl) && !string.IsNullOrEmpty(relayToken);

    private static AppConfig Load()
    {
        // 1. persistentDataPath/config.json — deployed via: adb push config.json /sdcard/Android/data/<pkg>/files/
        string persistent = Path.Combine(Application.persistentDataPath, "config.json");
        if (File.Exists(persistent))
            return Parse(File.ReadAllText(persistent), persistent);

        // 2. StreamingAssets/config.json — editor only (Android streaming assets need UnityWebRequest)
#if UNITY_EDITOR
        string streaming = Path.Combine(Application.streamingAssetsPath, "config.json");
        if (File.Exists(streaming))
            return Parse(File.ReadAllText(streaming), streaming);
#endif

        // 3. Defaults — no token, vision disabled
        Debug.LogWarning("[AppConfig] No config.json found; vision disabled.");
        return new AppConfig();
    }

    private static AppConfig Parse(string json, string source)
    {
        var cfg = JsonUtility.FromJson<AppConfig>(json);
        Debug.Log($"[AppConfig] Loaded from {source} (relayUrl={cfg.relayUrl}, prompts={cfg.prompts.Length})");
        return cfg;
    }
}
