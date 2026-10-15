using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.App.Camera;

public class DemoCameraController : ICameraController
{
    private bool _isConnected;
    private int _frameCounter;

    public string DisplayName => "Camera Giả lập (Demo)";

    public bool IsConnected => _isConnected;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = false;
        return Task.CompletedTask;
    }

    public Task<BitmapSource> GetLiveViewAsync(CancellationToken cancellationToken = default)
    {
        _frameCounter++;
        return Task.FromResult(CreateDemoFrame($"DEMO LIVE VIEW #{_frameCounter}", Colors.SlateGray));
    }

    public Task<BitmapSource> CaptureAsync(CancellationToken cancellationToken = default)
    {
        _frameCounter++;
        return Task.FromResult(CreateDemoFrame($"DEMO CAPTURED SHOT", Colors.DarkSlateBlue));
    }

    public ValueTask DisposeAsync()
    {
        _isConnected = false;
        return ValueTask.CompletedTask;
    }

    private static BitmapSource CreateDemoFrame(string text, Color background)
    {
        const int width = 1280;
        const int height = 720;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new System.Windows.Rect(0, 0, width, height));

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                48,
                Brushes.White,
                1.0);

            var origin = new System.Windows.Point((width - formattedText.Width) / 2, (height - formattedText.Height) / 2);
            dc.DrawText(formattedText, origin);
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}