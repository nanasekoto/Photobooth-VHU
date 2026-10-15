using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Photobooth.App.Rendering;
using QRCoder;

namespace Photobooth.App.Views;

public sealed class SessionQrWindow : Window
{
    private readonly TextBlock _status = new() { Text = "Loading...", FontSize = 20, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(12) };
    private readonly Image _qr = new() { Width = 320, Height = 320, Visibility = Visibility.Collapsed };
    private readonly TextBox _link = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(12) };
    private readonly Button _retry = new() { Content = "Thử lại", Visibility = Visibility.Collapsed, Margin = new Thickness(8) };
    private readonly Button _close = new() { Content = "Hủy chờ / Giữ ảnh trên máy", Margin = new Thickness(8) };
    private bool _finished;
    public event EventHandler? RetryRequested;
    public SessionQrWindow()
    {
        Title = "Nhận ảnh • Photobooth VHU"; Width = 500; Height = 620; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
        var content = new StackPanel { Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_status); content.Children.Add(_qr); content.Children.Add(_link);
        content.Children.Add(_retry); content.Children.Add(_close); Content = content;
        _retry.Click += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);
        _close.Click += (_, _) => Close();
    }
    public void Loading(string text = "Loading...")
    {
        _finished = false;
        _status.Text = text; _qr.Visibility = Visibility.Collapsed; _link.Visibility = Visibility.Collapsed;
        _retry.Visibility = Visibility.Collapsed; _close.Content = "Hủy chờ / Giữ ảnh trên máy";
    }
    public void Error(string text) { Loading(text); _finished = true; _retry.Visibility = Visibility.Visible; }
    public void Success(string url)
    {
        _finished = true;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        using var bytes = new MemoryStream(png.GetGraphic(10));
        _qr.Source = BitmapTools.Load(bytes);
        RenderOptions.SetBitmapScalingMode(_qr, BitmapScalingMode.NearestNeighbor);
        _qr.Visibility = Visibility.Visible; _link.Text = url; _link.Visibility = Visibility.Visible;
        _status.Text = "Quét mã QR để nhận đủ 5 ảnh"; _retry.Visibility = Visibility.Collapsed;
        _close.Content = "Đóng";
    }
    public void Progress(string text)
    {
        // Posted progress callbacks must not replace the final QR or an error/retry screen.
        if (!_finished) _status.Text = "Loading...\n" + text;
    }
}
