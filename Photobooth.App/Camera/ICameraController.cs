using System.Windows.Media.Imaging;

namespace Photobooth.App.Camera;

/// <summary>
/// Camera abstraction. Implement this with a vendor SDK for USB tether.
/// HDMI capture should be implemented as a separate live-view source because
/// HDMI alone normally does not provide shutter/autofocus control.
/// </summary>
public interface ICameraController : IAsyncDisposable
{
    string DisplayName { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task<BitmapSource> GetLiveViewAsync(CancellationToken cancellationToken = default);
    Task<BitmapSource> CaptureAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
