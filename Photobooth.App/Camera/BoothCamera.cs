using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Photobooth.App.Rendering;
using Rect = System.Windows.Rect;

namespace Photobooth.App.Camera;

public interface ILiveViewSource : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken token);
    BitmapSource? LatestFrame { get; }
}

public interface IPhotoCaptureController : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken token);
    Task AutoFocusAsync(CancellationToken token);
    Task CaptureToFileAsync(string path, CancellationToken token);
}

public sealed class HdmiLiveViewSource(int index, int width, int height) : ILiveViewSource
{
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private BitmapSource? _latest;
    public BitmapSource? LatestFrame => Volatile.Read(ref _latest);

    public async Task ConnectAsync(CancellationToken token)
    {
        if (_loop is not null) throw new InvalidOperationException("Nguồn video đã được mở.");
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _loop = Task.Run(() =>
        {
            try
            {
                using var capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
                if (!capture.IsOpened()) throw new InvalidOperationException($"Không mở được thiết bị video #{index}. Hãy chọn capture card trong cài đặt.");
                capture.FrameWidth = width;
                capture.FrameHeight = height;
                using var frame = new Mat();
                while (!_stop.IsCancellationRequested)
                {
                    if (!capture.Read(frame) || frame.Empty())
                        throw new IOException("Mất tín hiệu video. Kết nối lại capture card.");
                    var bitmap = frame.ToWriteableBitmap();
                    bitmap.Freeze();
                    Volatile.Write(ref _latest, bitmap);
                    ready.TrySetResult();
                }
            }
            catch (Exception ex) { ready.TrySetException(ex); Volatile.Write(ref _latest, null); }
        }, CancellationToken.None);
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), token); }
        catch { _stop.Cancel(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        _stop?.Cancel();
        if (_loop is not null)
        {
            // Some DirectShow drivers block Read after unplugging; do not freeze the UI waiting for them.
            await Task.WhenAny(_loop, Task.Delay(2000));
            if (_loop.IsCompleted) { _stop?.Dispose(); _loop = null; }
        }
        Volatile.Write(ref _latest, null);
    }
}

public sealed class SimulatedLiveViewSource : ILiveViewSource
{
    public BitmapSource? LatestFrame { get; private set; }
    public Task ConnectAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LatestFrame = SimulatedCaptureController.CreateDemo(1200, 800, "LIVE VIEW GIẢ LẬP");
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() { LatestFrame = null; return ValueTask.CompletedTask; }
}

public sealed class SimulatedCaptureController : IPhotoCaptureController
{
    private int _shot;
    public Task ConnectAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task AutoFocusAsync(CancellationToken token) => Task.Delay(100, token);
    public Task CaptureToFileAsync(string path, CancellationToken token) => BitmapTools.OnStaAsync(() =>
    {
        token.ThrowIfCancellationRequested();
        var image = CreateDemo(6000, 4000, $"ẢNH GIẢ LẬP {Interlocked.Increment(ref _shot)} • KHÔNG PHẢI ẢNH CANON");
        Services.ImageExporter.SaveJpeg(image, path, 95);
        return true;
    }, token);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static BitmapSource CreateDemo(int width, int height, string title) => BitmapTools.Render(width, height, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(16, 94, 181), Color.FromRgb(171, 221, 255), 35), null,
            new Rect(0, 0, width, height));
        var label = new FormattedText(title, CultureInfo.GetCultureInfo("vi-VN"), FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), width / 30d, Brushes.White, 1);
        dc.DrawText(label, new System.Windows.Point((width - label.Width) / 2, height / 2d));
    });
}
