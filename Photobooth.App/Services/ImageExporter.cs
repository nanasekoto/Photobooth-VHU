using System.IO;
using System.Windows.Media.Imaging;

namespace Photobooth.App.Services;

public static class ImageExporter
{
    public static void SaveJpeg(BitmapSource image, string path, int quality = 95)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
