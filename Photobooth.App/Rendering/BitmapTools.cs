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
