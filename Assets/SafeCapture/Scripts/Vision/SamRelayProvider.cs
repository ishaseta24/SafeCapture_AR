using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class SamRelayProvider : IVisionProvider
{
    private static int _frameCounter;

    public Task<VisionResult> DetectAsync(byte[] jpegBytes, int originalWidth, int originalHeight)
        => DetectAsync(jpegBytes, originalWidth, originalHeight, null);

    public async Task<VisionResult> DetectAsync(byte[] jpegBytes, int originalWidth, int originalHeight, string[] prompts)
    {
        var cfg = AppConfig.Instance;
        if (!cfg.IsConfigured)
        {
            Debug.LogWarning("[SamRelayProvider] No relay config — vision disabled.");
            return null;
        }

        // Use caller-supplied prompts (YOLO-detected labels) or fall back to full class list.
        var activePrompts = (prompts != null && prompts.Length > 0) ? prompts : cfg.prompts;

        string frameId = (++_frameCounter).ToString();
        var payload = new DetectRequest
        {
            frame_id       = frameId,
            image_jpeg_b64 = Convert.ToBase64String(jpegBytes),
            prompts        = activePrompts,
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

            var result = JsonUtility.FromJson<VisionResult>(req.downloadHandler.text);
            DecodeMasks(result);
            return result;
        }
        finally
        {
            req.Dispose();
        }
    }

    // Unpack the 1-bit-per-pixel base64 mask sent by the relay into a flat bool[].
    private static void DecodeMasks(VisionResult result)
    {
        if (result?.objects == null) return;
        foreach (var obj in result.objects)
        {
            if (string.IsNullOrEmpty(obj.maskB64) || obj.maskW <= 0 || obj.maskH <= 0) continue;
            try
            {
                byte[] packed = Convert.FromBase64String(obj.maskB64);
                int total = obj.maskW * obj.maskH;
                obj.mask = new bool[total];
                for (int i = 0; i < total; i++)
                    obj.mask[i] = (packed[i >> 3] & (1 << (7 - (i & 7)))) != 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SamRelayProvider] mask decode failed for '{obj.label}': {e.Message}");
            }
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
