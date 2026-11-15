using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// Calls the relay /mask endpoint to fetch pixel-precise Otsu masks for YOLO detections.
// No Meta API involved — pure sharp.js processing on the relay CPU.
public class MaskRelayProvider
{
    private static int _frameCounter;

    public async Task<VisionResult> GetMasksAsync(
        byte[] jpegBytes, VisionObject[] objects, int width, int height)
    {
        var cfg = AppConfig.Instance;
        if (!cfg.IsConfigured || jpegBytes == null || objects == null || objects.Length == 0)
            return null;

        string frameId = (++_frameCounter).ToString();
        var reqObjects = new MaskRequestObject[objects.Length];
        for (int i = 0; i < objects.Length; i++)
            reqObjects[i] = new MaskRequestObject { label = objects[i].label, box = objects[i].box };

        var payload = new MaskRequest
        {
            frame_id       = frameId,
            image_jpeg_b64 = Convert.ToBase64String(jpegBytes),
            objects        = reqObjects,
            width          = width,
            height         = height,
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        var req = new UnityWebRequest(cfg.relayUrl + "/mask", "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("X-Relay-Token", cfg.relayToken);
        req.timeout = 8;

        try
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MaskRelayProvider] {req.responseCode} {req.error}");
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
                Debug.LogWarning($"[MaskRelayProvider] mask decode failed '{obj.label}': {e.Message}");
            }
        }
    }

    [Serializable]
    private class MaskRequest
    {
        public string               frame_id;
        public string               image_jpeg_b64;
        public MaskRequestObject[]  objects;
        public int                  width;
        public int                  height;
    }

    [Serializable]
    private class MaskRequestObject
    {
        public string      label;
        public BoundingBox box;
    }
}
