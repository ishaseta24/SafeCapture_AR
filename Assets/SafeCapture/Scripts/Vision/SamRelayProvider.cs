using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class SamRelayProvider : IVisionProvider
{
    private static int _frameCounter;

    public async Task<VisionResult> DetectAsync(byte[] jpegBytes, int originalWidth, int originalHeight)
    {
        var cfg = AppConfig.Instance;
        if (!cfg.IsConfigured)
        {
            Debug.LogWarning("[SamRelayProvider] No relay config — vision disabled.");
            return null;
        }

        string frameId = (++_frameCounter).ToString();
        var payload = new DetectRequest
        {
            frame_id       = frameId,
            image_jpeg_b64 = Convert.ToBase64String(jpegBytes),
            prompts        = cfg.prompts,
            width          = originalWidth,
            height         = originalHeight,
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        var req = new UnityWebRequest(cfg.relayUrl + "/detect", "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("X-Relay-Token", cfg.relayToken);
        req.timeout = 10;

        try
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[SamRelayProvider] {req.responseCode} {req.error}");
                return null;
            }

            return JsonUtility.FromJson<VisionResult>(req.downloadHandler.text);
        }
        finally
        {
            req.Dispose();
        }
    }

    [Serializable]
    private class DetectRequest
    {
        public string   frame_id;
        public string   image_jpeg_b64;
        public string[] prompts;
        public int      width;
        public int      height;
    }
}
