using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Photobooth.App.Camera;
using Photobooth.App.Imaging;
using Photobooth.App.Models;
using Photobooth.App.Rendering;
using Photobooth.App.Services;
using Photobooth.App.Views;

namespace Photobooth.App;

public partial class MainWindow : Window
{
    private BoothSettings _settings = new();
    private BoothState _state;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _captureCancellation;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private ILiveViewSource? _liveView;
    private IPhotoCaptureController? _capture;
    private CubeLut? _lut;
    private SessionSnapshot? _session;
    private BitmapSource[]? _photos;
    private string? _workingDirectory;
    private string? _exportDirectory;
    private bool _exported;
    private bool _connected;
    private bool _connecting;
    private bool _initialized;
    private bool _previewBusy;
    private bool _closing;
    private bool _closeAllowed;
    private Task _operation = Task.CompletedTask;
    private SessionQrWindow? _qrWindow;

    public MainWindow()
    {
        InitializeComponent();
        _previewTimer.Tick += Preview_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = SettingsStore.Load();
            DefaultAssets.EnsureCreated(_settings);
            LayoutCombo.ItemsSource = new[] { "Lưới 2×2", "Dải dọc 1×4" };
            FilterCombo.ItemsSource = Enum.GetValues<FilterPreset>();
            LayoutCombo.SelectedIndex = (int)_settings.Layout;
            FilterCombo.SelectedItem = _settings.Filter;
            if (!string.IsNullOrWhiteSpace(_settings.LutPath))
            {
                try { _lut = CubeLut.Load(_settings.LutPath); LutLabel.Text = Path.GetFileName(_settings.LutPath); }
                catch (Exception ex) { _settings.Filter = FilterPreset.Original; FilterCombo.SelectedItem = _settings.Filter; StatusLabel.Text = ex.Message; }
            }
            LoadLiveFrame(); LoadStickerLibrary(); SettingsStore.Save(_settings);
            _initialized = true; UpdateModeLabel(); SetState(BoothState.Ready);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdateModeLabel() => ModeLabel.Text = _settings.CaptureMode == CaptureMode.Simulation
        ? "CHẾ ĐỘ GIẢ LẬP • Ảnh minh họa 6000×4000, không phải ảnh từ Canon."
        : "CANON R8 • Live view HDMI + chụp JPEG qua USB tether";

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_connecting || _state != BoothState.Ready || _closing) return;
        _operation = ConnectAsync(); await _operation;
    }

    private async Task ConnectAsync()
    {
        _connecting = true; ConnectButton.IsEnabled = false; SettingsButton.IsEnabled = false; CaptureButton.IsEnabled = false;
        try
        {
            await DisconnectAsync();
            _liveView = _settings.CaptureMode == CaptureMode.Simulation ? new SimulatedLiveViewSource()
                : new HdmiLiveViewSource(_settings.VideoDeviceIndex, _settings.VideoWidth, _settings.VideoHeight);
            _capture = _settings.CaptureMode == CaptureMode.Simulation ? new SimulatedCaptureController() : new CanonEdsdkCaptureController();
            StatusLabel.Text = "Đang kết nối nguồn hình và bộ điều khiển chụp...";
            await _liveView.ConnectAsync(_lifetime.Token);
            await _capture.ConnectAsync(_lifetime.Token);
            _connected = true; _previewTimer.Start();
            StatusLabel.Text = "Đã kết nối. Chọn bố cục và màu rồi bắt đầu chụp.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await DisconnectAsync(); if (!_closing) ShowError(ex); }
        finally { _connecting = false; if (!_closing) { ConnectButton.IsEnabled = true; SettingsButton.IsEnabled = true; CaptureButton.IsEnabled = _connected; } }
    }

    private async Task DisconnectAsync()
    {
        _previewTimer.Stop(); _connected = false;
        if (_capture is not null) { await _capture.DisposeAsync(); _capture = null; }
        if (_liveView is not null) { await _liveView.DisposeAsync(); _liveView = null; }
    }

    private async void Preview_Tick(object? sender, EventArgs e)
    {
        if (_previewBusy || _closing || _state is BoothState.Decorating or BoothState.Exporting) return;
        var raw = _liveView?.LatestFrame;
        if (raw is null)
        {
            if (_connected) { StatusLabel.Text = "Mất tín hiệu live view. Hãy kết nối lại."; CaptureButton.IsEnabled = false; }
            return;
        }
        _previewBusy = true;
        var filter = _session is not null && _state == BoothState.Capturing ? _session.Filter : _settings.Filter;
        var lut = _session is not null && _state == BoothState.Capturing ? _session.Lut : _lut;
        try
        {
            // Center crop HDMI's usual 16:9 feed into the 3:2 still-photo viewport, never stretch it.
            var cropWidth = Math.Min(raw.PixelWidth, (int)(raw.PixelHeight * 1.5));
            var cropHeight = Math.Min(raw.PixelHeight, (int)(raw.PixelWidth / 1.5));
            var cropped = new CroppedBitmap(raw, new Int32Rect((raw.PixelWidth - cropWidth) / 2, (raw.PixelHeight - cropHeight) / 2, cropWidth, cropHeight));
            cropped.Freeze();
            BitmapSource image = cropped;
            if (filter != FilterPreset.Original)
                image = await BitmapTools.OnStaAsync(() => ImageProcessor.Apply(BitmapTools.Resize(cropped, 900, 600), filter, lut), _lifetime.Token);
            if (!_closing && _state is BoothState.Ready or BoothState.Capturing) PreviewImage.Source = image;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closing) StatusLabel.Text = "Live view: " + ex.Message; }
        finally { _previewBusy = false; }
    }

    private void Options_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _state != BoothState.Ready) return;
        _settings.Layout = (PhotoLayout)Math.Max(0, LayoutCombo.SelectedIndex);
        _settings.Filter = FilterCombo.SelectedItem is FilterPreset value ? value : FilterPreset.Original;
        try { SettingsStore.Save(_settings); } catch (Exception ex) { ShowError(ex); }
    }

    private void LoadLut_Click(object sender, RoutedEventArgs e)
    {
        if (_state != BoothState.Ready) return;
        var dialog = new OpenFileDialog { Filter = "3D LUT|*.cube" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _lut = CubeLut.Load(dialog.FileName); _settings.LutPath = dialog.FileName;
            FilterCombo.SelectedItem = FilterPreset.CustomLut; LutLabel.Text = Path.GetFileName(dialog.FileName);
            SettingsStore.Save(_settings);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_connecting || _state != BoothState.Ready || _closing) return;
        var window = new SettingsWindow(_settings) { Owner = this };
        if (window.ShowDialog() != true) return;
        _settings = window.Settings; UpdateModeLabel();
        try { LoadLiveFrame(); _operation = DisconnectAsync(); await _operation; CaptureButton.IsEnabled = false; StatusLabel.Text = "Đã lưu cài đặt. Bấm Kết nối để áp dụng."; }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LoadLiveFrame() => LiveFrameImage.Source = BitmapTools.Load(_settings.SingleFramePath);
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => ShowSidebar(Sidebar.Visibility != Visibility.Visible);
    private void ShowSidebar(bool show)
    {
        Sidebar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(show ? 330 : 0);
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (_state != BoothState.Ready || !_connected || _connecting || _closing) return;
        _operation = CaptureSessionAsync(); await _operation;
    }

    private async Task CaptureSessionAsync()
    {
        _captureCancellation?.Dispose();
        _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _captureCancellation.Token;
        try
        {
            if (_settings.Filter == FilterPreset.CustomLut && _lut is null) throw new InvalidOperationException("Hãy nạp file .cube trước khi chọn CustomLut.");
            if (_liveView?.LatestFrame is null) throw new InvalidOperationException("Không có live view. Kết nối lại capture card trước.");
            var single = BitmapTools.Load(_settings.SingleFramePath);
            var frame = BitmapTools.Load(_settings.Layout == PhotoLayout.Grid2x2 ? _settings.GridFramePath : _settings.StripFramePath);
            _session = new SessionSnapshot(_settings.Layout, _settings.Filter, _lut, single, frame, _settings.OutputDirectory,
                _settings.JpegQuality, _settings.DriveParentId, _settings.GoogleCredentialsPath, _settings.SyncTimeoutSeconds);
            _workingDirectory = Path.Combine(SettingsStore.DataDirectory, "Working", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workingDirectory);
            _exportDirectory = null; _exported = false; _photos = null; StickerCanvas.Children.Clear();
            SetState(BoothState.Capturing);
            var sources = new List<string>();
            for (var shot = 1; shot <= CompositeRenderer.PhotoCount; shot++)
            {
                CountdownOverlay.Visibility = Visibility.Visible;
                Task? focus = null;
                for (var second = 3; second >= 1; second--)
                {
                    token.ThrowIfCancellationRequested();
                    CountdownText.Text = second.ToString(); ProgressLabel.Text = $"Ảnh {shot}/4 • Chụp sau {second} giây";
                    if (second == 1) focus = _capture!.AutoFocusAsync(token);
                    await Task.Delay(1000, token);
                }
                await focus!;
                token.ThrowIfCancellationRequested();
                CountdownText.Text = "0";
                var target = Path.Combine(_workingDirectory, $"source_{shot:00}.jpg");
                await _capture!.CaptureToFileAsync(target, token);
                sources.Add(target);
                CountdownOverlay.Visibility = Visibility.Collapsed;
                ProgressLabel.Text = $"Đã nhận {shot}/4 ảnh JPEG";
            }
            _previewTimer.Stop(); ProgressLabel.Text = "Đang dựng ảnh ghép để trang trí...";
            var session = _session;
            _photos = await BitmapTools.OnStaAsync(() => CompositeRenderer.PreparePhotos(sources, session, token), token);
            token.ThrowIfCancellationRequested();
            var preview = await BitmapTools.OnStaAsync(() => CompositeRenderer.Compose(_photos, session, Array.Empty<StickerPlacement>(), true), token);
            token.ThrowIfCancellationRequested();
            SetSceneSize(preview.PixelWidth, preview.PixelHeight); PreviewImage.Source = preview; LiveFrameImage.Visibility = Visibility.Collapsed;
            SetState(BoothState.Decorating); ShowSidebar(true); ToolsTabs.SelectedItem = StickerTab;
            ProgressLabel.Text = "Thêm sticker rồi bấm Hoàn tất & Nhận mã QR.";
        }
        catch (OperationCanceledException) { if (!_closing) { await DisconnectAsync(); ResetSession(); StatusLabel.Text = "Đã hủy. Ảnh nguồn còn trong vùng Working; kết nối lại để chụp phiên mới."; } }
        catch (Exception ex) { if (!_closing) { await DisconnectAsync(); ResetSession(); ShowError(ex); } }
        finally { CountdownOverlay.Visibility = Visibility.Collapsed; }
    }

    private void SetSceneSize(double width, double height)
    {
        SceneCanvas.Width = StickerCanvas.Width = PreviewImage.Width = LiveFrameImage.Width = width;
        SceneCanvas.Height = StickerCanvas.Height = PreviewImage.Height = LiveFrameImage.Height = height;
    }

    private void SetState(BoothState state)
    {
        _state = state;
        PreparationPanel.IsEnabled = state == BoothState.Ready;
        SettingsButton.IsEnabled = ConnectButton.IsEnabled = state == BoothState.Ready && !_connecting;
        StickerTab.IsEnabled = state == BoothState.Decorating;
        StickerCanvas.IsHitTestVisible = state == BoothState.Decorating && !_exported;
        StickerLibrary.IsEnabled = !_exported;
        CaptureButton.Visibility = state is BoothState.Ready or BoothState.Capturing ? Visibility.Visible : Visibility.Collapsed;
        CaptureButton.IsEnabled = state == BoothState.Ready && _connected && !_connecting;
        FinishButton.Visibility = state == BoothState.Decorating ? Visibility.Visible : Visibility.Collapsed;
        NewSessionButton.Visibility = state == BoothState.Decorating ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = state == BoothState.Capturing ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = state switch { BoothState.Ready => "01 • CHUẨN BỊ", BoothState.Capturing => "02 • CHỤP 4 ẢNH", BoothState.Decorating => "03 • TRANG TRÍ", _ => "04 • XUẤT & NHẬN ẢNH" };
    }

    private void LoadStickerLibrary()
    {
        StickerLibrary.Children.Clear();
        foreach (var file in Directory.EnumerateFiles(DefaultAssets.StickerDirectory, "*.png").OrderBy(path => path))
        {
            try
            {
                var bitmap = BitmapTools.Load(file);
                var button = new Button { Content = new Image { Source = bitmap, Width = 54, Height = 54, Stretch = Stretch.Uniform }, Margin = new Thickness(4), ToolTip = Path.GetFileNameWithoutExtension(file) };
                button.Click += (_, _) => AddSticker(bitmap); StickerLibrary.Children.Add(button);
            }
            catch (Exception ex) { StatusLabel.Text = $"Không đọc được sticker {Path.GetFileName(file)}: {ex.Message}"; }
        }
    }

    private void ImportSticker_Click(object sender, RoutedEventArgs e)
    {
        if (_state != BoothState.Decorating || _exported) return;
        var dialog = new OpenFileDialog { Filter = "PNG|*.png", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var file in dialog.FileNames)
            {
                var bitmap = BitmapTools.Load(file);
                var destination = Path.Combine(DefaultAssets.StickerDirectory, Guid.NewGuid().ToString("N") + ".png");
                File.Copy(file, destination); AddSticker(bitmap);
            }
            LoadStickerLibrary();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void AddSticker(BitmapSource bitmap)
    {
        if (_state != BoothState.Decorating || _exported) return;
        var ratio = bitmap.PixelWidth / (double)bitmap.PixelHeight;
        var width = Math.Min(240, SceneCanvas.Height * ratio * 0.4);
        var sticker = new Grid { Width = width, Height = width / ratio, Tag = bitmap };
        var image = new Image { Source = bitmap, Stretch = Stretch.Fill, Cursor = Cursors.SizeAll };
        sticker.Children.Add(image);
        Canvas.SetLeft(sticker, (SceneCanvas.Width - sticker.Width) / 2); Canvas.SetTop(sticker, (SceneCanvas.Height - sticker.Height) / 2);
        Point last = default;
        image.MouseLeftButtonDown += (_, ev) => { last = ev.GetPosition(StickerCanvas); image.CaptureMouse(); ev.Handled = true; };
        image.MouseMove += (_, ev) =>
        {
            if (!image.IsMouseCaptured || ev.LeftButton != MouseButtonState.Pressed) return;
            var current = ev.GetPosition(StickerCanvas);
            Canvas.SetLeft(sticker, Math.Clamp(Canvas.GetLeft(sticker) + current.X - last.X, 0, SceneCanvas.Width - sticker.Width));
            Canvas.SetTop(sticker, Math.Clamp(Canvas.GetTop(sticker) + current.Y - last.Y, 0, SceneCanvas.Height - sticker.Height));
            last = current;
        };
        image.MouseLeftButtonUp += (_, _) => image.ReleaseMouseCapture();
        var resize = new Thumb { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Background = Brushes.DodgerBlue };
        resize.DragDelta += (_, ev) =>
        {
            var maximum = Math.Min(SceneCanvas.Width - Canvas.GetLeft(sticker), (SceneCanvas.Height - Canvas.GetTop(sticker)) * ratio);
            var minimum = Math.Min(48, maximum);
            sticker.Width = Math.Clamp(sticker.Width + ev.HorizontalChange + ev.VerticalChange * ratio, minimum, maximum);
            sticker.Height = sticker.Width / ratio;
        };
        var remove = new Button { Content = "×", Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        remove.Click += (_, _) => StickerCanvas.Children.Remove(sticker);
        sticker.Children.Add(resize); sticker.Children.Add(remove); StickerCanvas.Children.Add(sticker);
    }

    private void ClearStickers_Click(object sender, RoutedEventArgs e) { if (_state == BoothState.Decorating && !_exported) StickerCanvas.Children.Clear(); }
    private StickerPlacement[] SnapshotStickers() => StickerCanvas.Children.OfType<Grid>().Select(sticker => new StickerPlacement((BitmapSource)sticker.Tag,
        Canvas.GetLeft(sticker) / SceneCanvas.Width, Canvas.GetTop(sticker) / SceneCanvas.Height, sticker.Width / SceneCanvas.Width, sticker.Height / SceneCanvas.Height)).ToArray();

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_state != BoothState.Decorating || _photos is null || _session is null || _closing) return;
        _operation = ExportAndShareAsync(); await _operation;
    }

    private async Task ExportAndShareAsync()
    {
        var stickers = SnapshotStickers(); var session = _session!; var photos = _photos!;
        SetState(BoothState.Exporting);
        _qrWindow = new SessionQrWindow { Owner = this };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var popup = _qrWindow;
        popup.Closed += (_, _) => { stop.Cancel(); closed.TrySetResult(); };
        popup.Show();
        var running = false;
        async Task RunAsync()
        {
            if (running || stop.IsCancellationRequested) return;
            running = true; popup.Loading();
            try
            {
                if (!_exported)
                {
                    _exportDirectory ??= ReserveSessionDirectory(session.OutputDirectory);
                    popup.Loading("Loading...\nĐang xuất 4 ảnh lẻ và ảnh ghép...");
                    await BitmapTools.OnStaAsync(() => CompositeRenderer.Export(photos, session, stickers, _exportDirectory, stop.Token), stop.Token);
                    _exported = true;
                }
                var progress = new Progress<string>(text => { if (popup.IsVisible && !stop.IsCancellationRequested) popup.Progress(text); });
                using var drive = new DriveDesktopShareService();
                var url = await drive.WaitAndShareAsync(_exportDirectory!, session, progress, stop.Token);
                if (!stop.IsCancellationRequested) popup.Success(url);
                StatusLabel.Text = "Đã tạo mã QR • " + _exportDirectory;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (popup.IsVisible) popup.Error(ex.Message + "\nẢnh đã xuất được giữ tại: " + (_exportDirectory ?? session.OutputDirectory)); }
            finally { running = false; }
        }
        Task pipeline = RunAsync();
        popup.RetryRequested += (_, _) => { if (!running) pipeline = RunAsync(); };
        await closed.Task; await pipeline;
        _qrWindow = null;
        if (!_closing)
        {
            SetState(BoothState.Decorating);
            FinishButton.Content = _exported ? "Hiện lại / Thử lại mã QR" : "Hoàn tất & Nhận mã QR";
            if (_exported) ProgressLabel.Text = "Đã lưu 5 ảnh. Có thể mở lại QR hoặc bắt đầu phiên mới.";
        }
    }

    private static string ReserveSessionDirectory(string parent)
    {
        Directory.CreateDirectory(parent);
        var time = DateTime.Now;
        while (Directory.Exists(Path.Combine(parent, time.ToString("yyyyMMdd_HHmmss")))) time = time.AddSeconds(1);
        var path = Path.Combine(parent, time.ToString("yyyyMMdd_HHmmss")); Directory.CreateDirectory(path); return path;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { CancelButton.IsEnabled = false; _captureCancellation?.Cancel(); }
    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        if (_state != BoothState.Decorating) return;
        if (!_exported && MessageBox.Show(this, "Ảnh ghép chưa xuất. Bắt đầu phiên mới? Ảnh nguồn vẫn được giữ trong vùng Working.", "Phiên mới", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        ResetSession();
    }
    private void ResetSession()
    {
        _photos = null; _session = null; _exported = false; _exportDirectory = null;
        StickerCanvas.Children.Clear(); SetSceneSize(1200, 800); PreviewImage.Source = null;
        LiveFrameImage.Visibility = Visibility.Visible; ToolsTabs.SelectedIndex = 0; CancelButton.IsEnabled = true;
        FinishButton.Content = "Hoàn tất & Nhận mã QR"; SetState(BoothState.Ready);
        ProgressLabel.Text = "Chọn bố cục và màu để chụp 4 ảnh.";
        if (_connected) _previewTimer.Start();
    }

    private void ShowError(Exception ex)
    {
        StatusLabel.Text = ex.Message;
        MessageBox.Show(this, ex.Message, "Photobooth VHU", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeAllowed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; IsEnabled = false; _previewTimer.Stop(); _lifetime.Cancel(); _captureCancellation?.Cancel(); _qrWindow?.Close();
        try { await _operation; await DisconnectAsync(); }
        catch { /* Closing must still release the UI if a device has disconnected. */ }
        finally { _closeAllowed = true; Close(); }
    }
}
