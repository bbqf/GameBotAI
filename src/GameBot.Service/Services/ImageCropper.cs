using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace GameBot.Service.Services;

internal sealed record CropBounds(int X, int Y, int Width, int Height);

[SupportedOSPlatform("windows")]
internal sealed class ImageCropper
{
    private const int MinSize = 16;

    public static (byte[] Png, bool WithinOnePixel) Crop(CaptureSession capture, CropBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (bounds.Width < MinSize || bounds.Height < MinSize)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Bounds must be at least 16x16.");

        using var ms = new MemoryStream(capture.Png, writable: false);
        using var bmp = new Bitmap(ms);
        if (bounds.X < 0 || bounds.Y < 0 || bounds.X + bounds.Width > bmp.Width || bounds.Y + bounds.Height > bmp.Height)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Bounds are outside the captured image.");

        var rect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        using var cropped = bmp.Clone(rect, bmp.PixelFormat);
        using var output = new MemoryStream();
        cropped.Save(output, ImageFormat.Png);
        var png = output.ToArray();
        var withinOnePixel = cropped.Width == bounds.Width && cropped.Height == bounds.Height;
        return (png, withinOnePixel);
    }
}
