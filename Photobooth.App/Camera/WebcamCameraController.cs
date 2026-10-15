using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace Photobooth.App.Camera;

public class WebcamCameraController : ICameraController
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private BitmapSource? _latestFrame;
    private readonly object _lock = new();

    public string DisplayName => "Webcam Laptop (Tích hợp)";

    public bool IsConnected => _capture != null && _capture.IsOpened();

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            _capture = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
            if (!_capture.IsOpened())
            {
                _capture.Open(0);
            }

            if (!_capture.IsOpened())
                throw new InvalidOperationException("Không thể mở Webcam trên máy tính.");

            _capture.FrameWidth = 1280;
            _capture.FrameHeight = 720;

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => FrameLoop(_cts.Token), cancellationToken);
        }, cancellationToken);
    }

    private void FrameLoop(CancellationToken token)
    {
        using var frame = new Mat();
        while (!token.IsCancellationRequested && _capture != null && _capture.IsOpened())
        {
            if (_capture.Read(frame) && !frame.Empty())
            {
                var bitmap = frame.ToWriteableBitmap();
                bitmap.Freeze();
                lock (_lock)
                {
                    _latestFrame = bitmap;
                }
            }
            Thread.Sleep(33); // ~30 FPS
        }
    }

    public Task<BitmapSource> GetLiveViewAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_latestFrame != null)
                return Task.FromResult(_latestFrame);
        }

        var placeholder = BitmapSource.Create(
            1280, 720, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null,
            new byte[1280 * 720 * 3], 1280 * 3);
        placeholder.Freeze();
        return Task.FromResult(placeholder);
    }

    public Task<BitmapSource> CaptureAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_latestFrame != null)
                return Task.FromResult(_latestFrame.Clone());
        }
        return GetLiveViewAsync(cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisconnectAsync();
        return ValueTask.CompletedTask;
    }
}