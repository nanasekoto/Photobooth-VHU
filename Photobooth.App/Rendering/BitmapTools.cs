using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.App.Rendering;

public static class BitmapTools
{
    public static BitmapSource Load(string path, int decodeWidth = 0)
    {
        using var stream = File.OpenRead(path);
        return Load(stream, decodeWidth);
    }

    public static BitmapSource Load(Stream stream, int decodeWidth = 0)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static BitmapSource Render(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource Resize(BitmapSource image, int width, int height) =>
        Render(width, height, dc => dc.DrawImage(image, new Rect(0, 0, width, height)));

    public static BitmapSource NormalizeLandscape(BitmapSource image, int width, int height)
    {
        if (width * 2 != height * 3) throw new ArgumentException("Canvas ảnh đơn phải có tỷ lệ 3:2.");
        // Crop whole 3x2 pixel blocks to keep exactly equal X/Y scaling, including 16:9 webcam sources.
        var block = Math.Min(image.PixelWidth / 3, image.PixelHeight / 2);
        if (block < 1) throw new InvalidDataException("Ảnh nguồn quá nhỏ.");
        var cropWidth = block * 3;
        var cropHeight = block * 2;
        var cropped = new CroppedBitmap(image, new Int32Rect((image.PixelWidth - cropWidth) / 2,
            (image.PixelHeight - cropHeight) / 2, cropWidth, cropHeight));
        cropped.Freeze();
        return Resize(cropped, width, height);
    }

    public static BitmapSource LoadFrame(string path, double expectedRatio, string label)
    {
        var image = Load(path);
        ValidateFrame(image, expectedRatio, label);
        return image;
    }

    public static void ValidateFrame(BitmapSource image, double expectedRatio, string label)
    {
        var actual = image.PixelWidth / (double)image.PixelHeight;
        if (Math.Abs(actual / expectedRatio - 1) > 0.01)
            throw new InvalidDataException($"{label}: PNG {image.PixelWidth}×{image.PixelHeight} sai tỷ lệ. Cần " +
                (Math.Abs(expectedRatio - 1.5) < 0.01 ? "khung ngang 3:2." : "khung dải dọc 3:8 (1 cột × 4 hàng)."));
    }

    // Each render job owns its WPF objects; only frozen bitmaps cross thread boundaries.
    public static Task<T> OnStaAsync<T>(Func<T> action, CancellationToken token = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(action()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "VHU image renderer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
