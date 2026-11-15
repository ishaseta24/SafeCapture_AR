using System.Threading.Tasks;

public interface IVisionProvider
{
    Task<VisionResult> DetectAsync(byte[] jpegBytes, int originalWidth, int originalHeight);
}
