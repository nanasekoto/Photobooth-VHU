using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Photobooth.App.Camera;
using Photobooth.App.Models;
using Photobooth.App.Rendering;
using Photobooth.App.Services;

namespace Photobooth.App.Views;

public sealed class SettingsWindow : Window
{
    public BoothSettings Settings { get; }
    private readonly CancellationTokenSource _lifetime = new();
    private bool _signingIn;

    public SettingsWindow(BoothSettings source)
    {
        Settings = JsonSerializer.Deserialize<BoothSettings>(JsonSerializer.Serialize(source))!;
        Title = "Cài đặt • Photobooth VHU"; Width = 680; Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Label(string text) => stack.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4), TextWrapping = TextWrapping.Wrap });
        TextBox Field(string text) { var box = new TextBox { Text = text }; stack.Children.Add(box); return box; }
        Button Button(string text, Action action)
        {
            var button = new Button { Content = text }; button.Click += (_, _) => action(); stack.Children.Add(button); return button;
        }
        void FileField(string title, string path, string filter, Action<string> assign, double? frameRatio = null)
        {
            Label(title); var box = Field(path); box.IsReadOnly = true;
            Button("Chọn file", () =>
            {
                var picker = new OpenFileDialog { Filter = filter };
                if (picker.ShowDialog(this) != true) return;
                try
                {
                    if (frameRatio is { } ratio) _ = BitmapTools.LoadFrame(picker.FileName, ratio, title);
                    assign(picker.FileName); box.Text = picker.FileName;
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không đọc được file"); }
            });
        }
        Label("Nguồn Live View (độc lập với nguồn chụp)");
        var preview = new ComboBox { ItemsSource = new[] { "Video thật • Webcam / HDMI Capture Card", "Hình minh họa giả lập • Không có người thật" }, SelectedIndex = (int)Settings.PreviewSource };
        stack.Children.Add(preview);
        Label("Nguồn ảnh chụp");
        var mode = new ComboBox { ItemsSource = new[] { "Giả lập 6000×4000 từ preview (có nhãn, không phải Canon)", "Canon R8 • JPEG qua USB tether", "Ảnh từ frame webcam/capture card (không phải Canon)" }, SelectedIndex = (int)Settings.CaptureMode };
        stack.Children.Add(mode);
        Label("Thiết bị HDMI capture card (DirectShow)");
        var video = new ComboBox(); stack.Children.Add(video);
        try { video.ItemsSource = VideoDeviceCatalog.List(); video.SelectedItem = ((IReadOnlyList<VideoDevice>)video.ItemsSource).FirstOrDefault(d => d.Index == Settings.VideoDeviceIndex); }
        catch (Exception ex) { stack.Children.Add(new TextBlock { Text = "Không liệt kê được thiết bị: " + ex.Message, TextWrapping = TextWrapping.Wrap }); }
        Label("Chỉ số thiết bị (có thể nhập thủ công nếu danh sách trống)");
        var indexBox = Field(Settings.VideoDeviceIndex.ToString());
        video.SelectionChanged += (_, _) => { if (video.SelectedItem is VideoDevice device) indexBox.Text = device.Index.ToString(); };
        Label("Thư mục xuất trong Google Drive for desktop");
        var output = Field(Settings.OutputDirectory);
        Button("Chọn thư mục lưu", () => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) output.Text = dialog.FolderName; });
        FileField("Frame ảnh đơn • 3:2", Settings.SingleFramePath, "PNG|*.png", path => Settings.SingleFramePath = path, 1.5);
        FileField("Frame lưới 2×2 • 3:2", Settings.GridFramePath, "PNG|*.png", path => Settings.GridFramePath = path, 1.5);
        FileField("Frame dải dọc 1×4 • 3:8", Settings.StripFramePath, "PNG|*.png", path => Settings.StripFramePath = path, 0.375);
        Label("JPEG Quality (1–100; tách biệt với kích thước ảnh cố định 70%)");
        var quality = Field(Settings.JpegQuality.ToString());
        Label("Folder ID thư mục cha trên Google Drive (cùng thư mục với đường dẫn lưu)");
        var parent = Field(Settings.DriveParentId);
        FileField("OAuth Desktop App JSON", Settings.GoogleCredentialsPath, "JSON|*.json", path => Settings.GoogleCredentialsPath = path);
        Label("Google cần quyền Drive để tìm ảnh do Drive for desktop tạo. Thư mục phiên sẽ được chia sẻ cho bất kỳ ai có liên kết; ảnh không cần đăng nhập để mở.");
        var signIn = new Button { Content = "Đăng nhập Google" }; stack.Children.Add(signIn);
        signIn.Click += async (_, _) =>
        {
            if (_signingIn) return;
            _signingIn = true; signIn.IsEnabled = false;
            try { await new GoogleAuthorization().SignInAsync(Settings.GoogleCredentialsPath, _lifetime.Token); MessageBox.Show(this, "Đã đăng nhập Google."); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (IsVisible) MessageBox.Show(this, ex.Message, "Đăng nhập Google"); }
            finally { _signingIn = false; signIn.IsEnabled = true; }
        };
        Label("Thời gian chờ đồng bộ tối đa (30–1800 giây)");
        var timeout = Field(Settings.SyncTimeoutSeconds.ToString());
        Button("Lưu cài đặt", () =>
        {
            try
            {
                if (_signingIn) throw new InvalidOperationException("Hoàn tất đăng nhập Google trước khi lưu.");
                if (!int.TryParse(indexBox.Text, out var device) || device < 0) throw new InvalidDataException("Chỉ số video phải từ 0 trở lên.");
                if (!int.TryParse(quality.Text, out var q) || q < 1 || q > 100) throw new InvalidDataException("JPEG Quality phải từ 1 đến 100.");
                if (!int.TryParse(timeout.Text, out var t) || t < 30 || t > 1800) throw new InvalidDataException("Thời gian chờ phải từ 30 đến 1800 giây.");
                if (!Path.IsPathFullyQualified(output.Text.Trim())) throw new InvalidDataException("Cần đường dẫn lưu tuyệt đối.");
                Settings.OutputDirectory = output.Text.Trim(); Settings.VideoDeviceIndex = device;
                Settings.CaptureMode = (CaptureMode)mode.SelectedIndex; Settings.DriveParentId = parent.Text.Trim();
                Settings.PreviewSource = (LiveViewMode)preview.SelectedIndex;
                if (Settings.CaptureMode == CaptureMode.VideoFrame && Settings.PreviewSource != LiveViewMode.VideoDevice)
                    throw new InvalidDataException("Chụp frame video cần chọn nguồn Live View thật.");
                _ = BitmapTools.LoadFrame(Settings.SingleFramePath, 1.5, "Frame ảnh đơn");
                _ = BitmapTools.LoadFrame(Settings.GridFramePath, 1.5, "Frame lưới");
                _ = BitmapTools.LoadFrame(Settings.StripFramePath, 0.375, "Frame dải dọc");
                Settings.JpegQuality = q; Settings.SyncTimeoutSeconds = t;
                SettingsStore.Save(Settings); DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cài đặt chưa hợp lệ"); }
        });
        Closed += (_, _) => _lifetime.Cancel();
    }
}
