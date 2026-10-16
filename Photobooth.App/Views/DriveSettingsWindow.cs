using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Photobooth.App.Models;
using Photobooth.App.Services;

namespace Photobooth.App.Views;

/// <summary>Sharing-only settings: never changes camera, colour, layout or stickers.</summary>
public sealed class DriveSettingsWindow : Window
{
    public BoothSettings Settings { get; }
    private readonly CancellationTokenSource _lifetime = new();
    private bool _signingIn;

    public DriveSettingsWindow(BoothSettings source, string? currentDirectory)
    {
        Settings = JsonSerializer.Deserialize<BoothSettings>(JsonSerializer.Serialize(source))!;
        Title = "Sửa Drive / Đăng nhập • Giữ nguyên phiên chụp";
        Width = 650; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        TextBox Field(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) });
            var box = new TextBox { Text = value }; panel.Children.Add(box); return box;
        }
        var notice = new TextBlock { Text = "Ảnh và trang trí của khách được giữ nguyên. Nếu đổi thư mục lưu, 5 file sẽ được xuất lại vào vị trí mới khi bấm Thử lại; bản cũ được giữ.\n\nẢnh đã lưu: " + (currentDirectory ?? "Chưa xuất"), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(notice);
        var output = Field("Thư mục lưu trong Google Drive for desktop", Settings.OutputDirectory);
        var folder = new Button { Content = "Chọn thư mục" }; panel.Children.Add(folder);
        folder.Click += (_, _) => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) output.Text = picker.FolderName; };
        var parent = Field("Folder ID thư mục cha trên Drive (phải khớp đường dẫn lưu)", Settings.DriveParentId);
        var credentials = Field("OAuth Desktop App JSON", Settings.GoogleCredentialsPath);
        var file = new Button { Content = "Chọn OAuth JSON" }; panel.Children.Add(file);
        file.Click += (_, _) => { var picker = new OpenFileDialog { Filter = "JSON|*.json" }; if (picker.ShowDialog(this) == true) credentials.Text = picker.FileName; };
        var timeout = Field("Thời gian chờ (30–1800 giây)", Settings.SyncTimeoutSeconds.ToString());
        panel.Children.Add(new TextBlock { Text = "Thư mục phiên được chia sẻ cho bất kỳ ai có liên kết. Dùng cùng tài khoản với Drive for desktop.", TextWrapping = TextWrapping.Wrap });
        var signIn = new Button { Content = "Đăng nhập / Cấp quyền Google lại" }; panel.Children.Add(signIn);
        signIn.Click += async (_, _) =>
        {
            if (_signingIn) return;
            _signingIn = true; signIn.IsEnabled = false;
            try
            {
                await new GoogleAuthorization().SignInAsync(credentials.Text.Trim(), _lifetime.Token);
                if (IsVisible) MessageBox.Show(this, "Đã đăng nhập. Bấm Lưu rồi Thử lại mã QR.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (IsVisible) MessageBox.Show(this, ex.Message, "Google Drive"); }
            finally { _signingIn = false; signIn.IsEnabled = true; }
        };
        var save = new Button { Content = "Lưu và trở lại phiên hiện tại" }; panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            try
            {
                if (_signingIn) throw new InvalidOperationException("Hoàn tất đăng nhập trước khi lưu.");
                if (!Path.IsPathFullyQualified(output.Text.Trim())) throw new InvalidDataException("Cần đường dẫn lưu tuyệt đối.");
                if (string.IsNullOrWhiteSpace(parent.Text)) throw new InvalidDataException("Cần Folder ID thư mục cha.");
                if (!int.TryParse(timeout.Text, out var seconds) || seconds < 30 || seconds > 1800) throw new InvalidDataException("Thời gian chờ phải từ 30 đến 1800 giây.");
                Settings.OutputDirectory = output.Text.Trim(); Settings.DriveParentId = parent.Text.Trim();
                Settings.GoogleCredentialsPath = credentials.Text.Trim(); Settings.SyncTimeoutSeconds = seconds;
                SettingsStore.Save(Settings); DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cấu hình Drive"); }
        };
        Closed += (_, _) => _lifetime.Cancel();
    }
}
