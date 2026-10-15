using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.App.Imaging;

public static class ImageProcessor
{
    public static BitmapSource Apply(BitmapSource source, FilterPreset preset, CubeLut? lut = null)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            float b = pixels[i] / 255f;
            float g = pixels[i + 1] / 255f;
            float r = pixels[i + 2] / 255f;
            (r, g, b) = preset switch
            {
                FilterPreset.Warm => Warm(r, g, b),
                FilterPreset.Retro => Retro(r, g, b),
                FilterPreset.BlackAndWhite => BlackAndWhite(r, g, b),
                FilterPreset.Pastel => Pastel(r, g, b),
                FilterPreset.CustomLut when lut is not null => lut.Sample(r, g, b),
                _ => (r, g, b)
            };
            pixels[i] = ToByte(b);
            pixels[i + 1] = ToByte(g);
            pixels[i + 2] = ToByte(r);
        }

        var output = new WriteableBitmap(width, height, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null);
        output.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        output.Freeze();
        return output;
    }

    private static (float R, float G, float B) Warm(float r, float g, float b)
        => (Math.Clamp(r * 1.05f + 0.035f, 0, 1), Math.Clamp(g * 1.01f + 0.005f, 0, 1), Math.Clamp(b * 0.92f, 0, 1));

    private static (float R, float G, float B) Retro(float r, float g, float b)
    {
        var nr = Contrast(r, 1.08f) * 0.96f + 0.035f;
        var ng = Contrast(g, 1.02f) * 0.95f + 0.025f;
        var nb = Contrast(b, 0.94f) * 0.88f + 0.045f;
        return (Math.Clamp(nr, 0, 1), Math.Clamp(ng, 0, 1), Math.Clamp(nb, 0, 1));
    }

    private static (float R, float G, float B) BlackAndWhite(float r, float g, float b)
    {
        var y = Math.Clamp(0.2126f * r + 0.7152f * g + 0.0722f * b, 0, 1);
        return (y, y, y);
    }

    private static (float R, float G, float B) Pastel(float r, float g, float b)
        => (Math.Clamp(r * 0.72f + 0.28f, 0, 1), Math.Clamp(g * 0.72f + 0.28f, 0, 1), Math.Clamp(b * 0.72f + 0.28f, 0, 1));

    private static float Contrast(float value, float amount) => (value - 0.5f) * amount + 0.5f;
    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
