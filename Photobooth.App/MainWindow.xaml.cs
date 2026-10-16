using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
    private readonly StaRenderWorker _previewWorker = new("VHU continuous live preview");
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private CancellationTokenSource? _captureCancellation;
    private ILiveViewSource? _liveView;
    private IPhotoCaptureController? _capture;
    private CubeLut? _lut;
    private SessionSnapshot? _session;
    private ReviewWindow? _review;
    private BitmapSource? _lastPreviewSource;
    private bool _previewBusy;
    private int _previewGeneration;
    private bool _connected;
    private bool _connecting;
    private bool _initialized;
    private bool _closing;
    private bool _closeAllowed;
    private Task _operation = Task.CompletedTask;

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.2.0";
        var architecture = Environment.Is64BitProcess ? "x64" : "x86";
        Title = $"Photobooth VHU v{version} • .NET 8 • {architecture}";
        _previewTimer.Tick += Preview_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = SettingsStore.Load(); DefaultAssets.EnsureCreated(_settings);
            LayoutCombo.ItemsSource = new[] { "Lưới 2×2", "Dải dọc — 1 cột × 4 hàng" };
            FilterCombo.ItemsSource = Enum.GetValues<FilterPreset>();
            LayoutCombo.SelectedIndex = (int)_settings.Layout; FilterCombo.SelectedItem = _settings.Filter;
            if (!string.IsNullOrWhiteSpace(_settings.LutPath))
            {
                try { _lut = CubeLut.Load(_settings.LutPath); LutLabel.Text = Path.GetFileName(_settings.LutPath); }
                catch (Exception ex) { _settings.Filter = FilterPreset.Original; FilterCombo.SelectedItem = _settings.Filter; StatusLabel.Text = ex.Message; }
            }
            LoadLiveFrame(); SettingsStore.Save(_settings);
            _initialized = true; UpdateModeLabel(); SetState(BoothState.Ready);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdateModeLabel()
    {
        var preview = _settings.PreviewSource == LiveViewMode.VideoDevice ? "Live View thật: Webcam / Capture Card" : "Live View minh họa tĩnh";
        var capture = _settings.CaptureMode switch
        {
            CaptureMode.Canon => "Chụp JPEG Canon qua USB tether",
            CaptureMode.VideoFrame => "Chụp frame video • Không phải ảnh Canon",
            _ => "Chụp giả lập 6000×4000 từ preview • Có nhãn, không phải Canon"
        };
        ModeLabel.Text = preview + "\n\n" + capture;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_connecting || _state != BoothState.Ready || _closing) return;
        _operation = ConnectAsync(); await _operation;
    }
    private async Task ConnectAsync()
    {
        _connecting = true; SetState(BoothState.Ready);
        try
        {
            await DisconnectAsync();
            _liveView = _settings.PreviewSource == LiveViewMode.VideoDevice
                ? new HdmiLiveViewSource(_settings.VideoDeviceIndex, _settings.VideoWidth, _settings.VideoHeight)
                : new SimulatedLiveViewSource();
            await _liveView.ConnectAsync(_lifetime.Token);
            _capture = _settings.CaptureMode switch
            {
                CaptureMode.Canon => new CanonEdsdkCaptureController(),
                CaptureMode.VideoFrame => new VideoFrameCaptureController(() => _liveView?.LatestFrame),
                _ => new SimulatedCaptureController(() => _liveView?.LatestFrame)
            };
            await _capture.ConnectAsync(_lifetime.Token);
            _connected = true; _lastPreviewSource = null; _previewTimer.Start();
            StatusLabel.Text = "Đã kết nối. Live view tiếp tục hoạt động khi đếm ngược.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await DisconnectAsync(); if (!_closing) ShowError(ex); }
        finally { _connecting = false; if (!_closing) SetState(BoothState.Ready); }
    }
    private async Task DisconnectAsync()
    {
        _previewGeneration++; _previewTimer.Stop(); _connected = false; _lastPreviewSource = null;
        if (_capture is not null) { await _capture.DisposeAsync(); _capture = null; }
        if (_liveView is not null) { await _liveView.DisposeAsync(); _liveView = null; }
    }

    private async void Preview_Tick(object? sender, EventArgs e)
    {
        // Capturing remains eligible; only review replaces the live preview.
        if (_previewBusy || _closing || _state is BoothState.Decorating or BoothState.Exporting) return;
        var raw = _liveView?.LatestFrame;
        if (raw is null)
        {
            if (_connected) { StatusLabel.Text = "Mất tín hiệu video. Hãy kết nối lại."; CaptureButton.IsEnabled = false; }
            return;
        }
        if (ReferenceEquals(raw, _lastPreviewSource)) return;
        _previewBusy = true; _lastPreviewSource = raw;
        var generation = _previewGeneration;
        var filter = _state == BoothState.Capturing && _session is not null ? _session.Filter : _settings.Filter;
        var lut = _state == BoothState.Capturing && _session is not null ? _session.Lut : _lut;
        try
        {
            // A single long-lived worker and one in-flight job: newer frames replace old ones, no backlog.
            var image = await _previewWorker.InvokeAsync(() => ImageProcessor.Apply(BitmapTools.NormalizeLandscape(raw, 900, 600), filter, lut), _lifetime.Token);
            if (!_closing && generation == _previewGeneration && _state is BoothState.Ready or BoothState.Capturing)
                PreviewImage.Source = image;
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
        _lastPreviewSource = null; _previewGeneration++;
        try { SettingsStore.Save(_settings); } catch (Exception ex) { ShowError(ex); }
    }
    private void LoadLut_Click(object sender, RoutedEventArgs e)
    {
        if (_state != BoothState.Ready) return;
        var picker = new OpenFileDialog { Filter = "3D LUT|*.cube" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            _lut = CubeLut.Load(picker.FileName); _settings.LutPath = picker.FileName;
            _lastPreviewSource = null; _previewGeneration++;
            FilterCombo.SelectedItem = FilterPreset.CustomLut; LutLabel.Text = Path.GetFileName(picker.FileName); SettingsStore.Save(_settings);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_connecting || _state != BoothState.Ready || _closing) return;
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _settings = dialog.Settings; UpdateModeLabel();
        try { LoadLiveFrame(); _operation = DisconnectAsync(); await _operation; SetState(BoothState.Ready); StatusLabel.Text = "Đã lưu. Bấm Kết nối để áp dụng nguồn camera mới."; }
        catch (Exception ex) { ShowError(ex); }
    }
    private void LoadLiveFrame() => LiveFrameImage.Source = BitmapTools.LoadFrame(_settings.SingleFramePath, 1.5, "Frame ảnh đơn");
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        var show = Sidebar.Visibility != Visibility.Visible;
        Sidebar.Visibility = show ? Visibility.Visible : Visibility.Collapsed; SidebarColumn.Width = new GridLength(show ? 330 : 0);
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
            if (_settings.Filter == FilterPreset.CustomLut && _lut is null) throw new InvalidOperationException("Nạp LUT .cube trước khi chụp với CustomLut.");
            if (_liveView?.LatestFrame is null) throw new InvalidOperationException("Không có live view. Hãy kết nối lại.");
            _session = new SessionSnapshot(_settings.Layout, _settings.Filter, _lut,
                BitmapTools.LoadFrame(_settings.SingleFramePath, 1.5, "Frame ảnh đơn"),
                BitmapTools.LoadFrame(_settings.GridFramePath, 1.5, "Frame lưới"),
                BitmapTools.LoadFrame(_settings.StripFramePath, 0.375, "Frame dải dọc"),
                _settings.OutputDirectory, _settings.JpegQuality);
            var working = Path.Combine(SettingsStore.DataDirectory, "Working", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(working); SetState(BoothState.Capturing);
            var sources = new List<string>();
            for (var shot = 1; shot <= CompositeRenderer.PhotoCount; shot++)
            {
                CountdownOverlay.Visibility = Visibility.Visible;
                Task? focus = null;
                for (var second = 3; second >= 1; second--)
                {
                    if (_liveView?.LatestFrame is null) throw new IOException("Mất live view trong lúc đếm ngược. Kết nối lại nguồn video trước khi chụp tiếp.");
                    token.ThrowIfCancellationRequested(); CountdownText.Text = second.ToString();
                    ProgressLabel.Text = $"Ảnh {shot}/4 • Chụp sau {second} giây";
                    if (second == 1) focus = _capture!.AutoFocusAsync(token);
                    await Task.Delay(1000, token);
                }
                await focus!; token.ThrowIfCancellationRequested(); CountdownText.Text = "0";
                var path = Path.Combine(working, $"source_{shot:00}.jpg");
                await _capture!.CaptureToFileAsync(path, token); sources.Add(path);
                CountdownOverlay.Visibility = Visibility.Collapsed; ProgressLabel.Text = $"Đã nhận {shot}/4 ảnh";
            }
            _previewTimer.Stop(); _previewGeneration++; CountdownOverlay.Visibility = Visibility.Collapsed;
            ProgressLabel.Text = "Đang chuẩn bị popup Xem lại & Trang trí...";
            token.ThrowIfCancellationRequested(); SetState(BoothState.Decorating);
            _review = new ReviewWindow(sources, _session, () => _settings, settings => { _settings = settings; }, _lifetime.Token) { Owner = this };
            _review.ShowDialog(); _review = null;
            StatusLabel.Text = "Phiên đã đóng. Ảnh nguồn được giữ trong Working; sẵn sàng cho lượt tiếp theo.";
        }
        catch (OperationCanceledException) { if (!_closing) { await DisconnectAsync(); StatusLabel.Text = "Đã hủy chụp. Kết nối lại trước phiên mới."; } }
        catch (Exception ex) { if (!_closing) { await DisconnectAsync(); ShowError(ex); } }
        finally
        {
            _session = null; CountdownOverlay.Visibility = Visibility.Collapsed;
            if (!_closing)
            {
                CancelButton.IsEnabled = true; _lastPreviewSource = null; SetState(BoothState.Ready);
                if (_connected) _previewTimer.Start();
            }
        }
    }

    private void SetState(BoothState state)
    {
        _state = state; PreparationPanel.IsEnabled = state == BoothState.Ready && !_connecting;
        SettingsButton.IsEnabled = ConnectButton.IsEnabled = state == BoothState.Ready && !_connecting;
        CaptureButton.IsEnabled = state == BoothState.Ready && _connected && !_connecting;
        CancelButton.Visibility = state == BoothState.Capturing ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = state switch { BoothState.Ready => "01 • CHUẨN BỊ", BoothState.Capturing => "02 • CHỤP 4 ẢNH", _ => "03 • XEM LẠI & TRANG TRÍ" };
        if (state == BoothState.Ready) ProgressLabel.Text = "Chọn bố cục mặc định và màu, rồi chụp 4 ảnh.";
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { CancelButton.IsEnabled = false; _captureCancellation?.Cancel(); }
    private void ShowError(Exception ex)
    {
        StatusLabel.Text = ex.Message; MessageBox.Show(this, ex.Message, "Photobooth VHU", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeAllowed) return;
        e.Cancel = true; if (_closing) return;
        _closing = true; _previewTimer.Stop(); _lifetime.Cancel(); _captureCancellation?.Cancel(); _review?.Close();
        try { await _operation; await DisconnectAsync(); await _previewWorker.DisposeAsync(); }
        catch { /* Finish closing even if a device has already disconnected. */ }
        finally { _closeAllowed = true; Close(); }
    }
}
