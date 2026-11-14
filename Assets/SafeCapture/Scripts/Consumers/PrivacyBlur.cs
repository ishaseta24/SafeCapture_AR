using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

// Pixelates selected detections before the captured image is JPEG-encoded.
[DefaultExecutionOrder(-100)]
public class PrivacyBlur : MonoBehaviour
{
    [SerializeField] private FrameCapture _frameCapture;
    [SerializeField, Min(1)] private int _blockSize = 16;
    [SerializeField, Min(0)] private int _paddingPixels = 8;

    private ObjectSelectionManager _selectionManager;
    private readonly List<int> _selectedTrackIds = new List<int>();

    private void Awake()
    {
        _selectionManager = GetComponent<ObjectSelectionManager>();
    }

    private void OnEnable()
    {
        if (_frameCapture != null)
            _frameCapture.PreEncodeFilter += PixelateSelected;
    }

    private void OnDisable()
    {
        if (_frameCapture != null)
            _frameCapture.PreEncodeFilter -= PixelateSelected;
    }

    private void PixelateSelected(Texture2D texture)
    {
        if (_selectionManager == null || !_selectionManager.HasSelection)
            return;

        var pixels = texture.GetPixelData<byte>(0);
        int bytesPerPixel = pixels.Length / (texture.width * texture.height);
        if (bytesPerPixel < 3)
            return;

        if (_selectionManager.ShouldMaskFullFrame)
        {
            PixelateRegion(pixels, texture.width, texture.height, bytesPerPixel,
                0, 0, texture.width - 1, texture.height - 1, Mathf.Max(_blockSize, 24));
            return;
        }

        _selectionManager.CopySelectedTrackIds(_selectedTrackIds);
        foreach (int trackId in _selectedTrackIds)
        {
            BoundingBox box;
            int sourceWidth;
            int sourceHeight;
            if (!_selectionManager.TryGetLastKnownDetection(
                    trackId, out box, out sourceWidth, out sourceHeight)
                || box == null || sourceWidth <= 0 || sourceHeight <= 0)
                continue;

            PixelateBox(pixels, texture.width, texture.height, bytesPerPixel,
                box, sourceWidth, sourceHeight, _blockSize, _paddingPixels);
        }
    }

    private static void PixelateBox(
        NativeArray<byte> pixels,
        int width,
        int height,
        int bytesPerPixel,
        BoundingBox box,
        int sourceWidth,
        int sourceHeight,
        int blockSize,
        int padding)
    {
        float scaleX = (float)width / sourceWidth;
        float scaleY = (float)height / sourceHeight;

        int left = Mathf.Clamp(Mathf.FloorToInt(box.x1 * scaleX) - padding, 0, width - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(box.x2 * scaleX) + padding, 0, width - 1);
        int top = Mathf.Clamp(Mathf.FloorToInt(box.y1 * scaleY) - padding, 0, height - 1);
        int bottom = Mathf.Clamp(Mathf.CeilToInt(box.y2 * scaleY) + padding, 0, height - 1);

        // Detection coordinates use a top-left origin; texture memory uses bottom-left.
        int textureBottom = height - 1 - bottom;
        int textureTop = height - 1 - top;
        PixelateRegion(pixels, width, height, bytesPerPixel,
            left, textureBottom, right, textureTop, Mathf.Max(1, blockSize));
    }

    private static void PixelateRegion(
        NativeArray<byte> pixels,
        int width,
        int height,
        int bytesPerPixel,
        int left,
        int bottom,
        int right,
        int top,
        int blockSize)
    {
        left = Mathf.Clamp(left, 0, width - 1);
        right = Mathf.Clamp(right, 0, width - 1);
        bottom = Mathf.Clamp(bottom, 0, height - 1);
        top = Mathf.Clamp(top, 0, height - 1);
        if (left > right || bottom > top)
            return;

        for (int blockY = bottom; blockY <= top; blockY += blockSize)
        {
            int blockTop = Mathf.Min(blockY + blockSize - 1, top);
            for (int blockX = left; blockX <= right; blockX += blockSize)
            {
                int blockRight = Mathf.Min(blockX + blockSize - 1, right);
                int red = 0, green = 0, blue = 0, count = 0;

                for (int y = blockY; y <= blockTop; y++)
                {
                    for (int x = blockX; x <= blockRight; x++)
                    {
                        int offset = (y * width + x) * bytesPerPixel;
                        red += pixels[offset];
                        green += pixels[offset + 1];
                        blue += pixels[offset + 2];
                        count++;
                    }
                }

                byte avgRed = (byte)(red / count);
                byte avgGreen = (byte)(green / count);
                byte avgBlue = (byte)(blue / count);
                for (int y = blockY; y <= blockTop; y++)
                {
                    for (int x = blockX; x <= blockRight; x++)
                    {
                        int offset = (y * width + x) * bytesPerPixel;
                        pixels[offset] = avgRed;
                        pixels[offset + 1] = avgGreen;
                        pixels[offset + 2] = avgBlue;
                    }
                }
            }
        }
    }
}
