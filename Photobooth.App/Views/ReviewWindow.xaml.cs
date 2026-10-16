using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Photobooth.App.Models;
using Photobooth.App.Rendering;
using Photobooth.App.Services;

namespace Photobooth.App.Views;

public partial class ReviewWindow : Window
{
    private sealed class EditorSticker(BitmapSource image)
    {
        public BitmapSource Image { get; } = image;
        public double X { get; set; } = 0.4;
        public double Y { get; set; } = 0.4;
        public double Width { get; set; } = 0.2;
    }
    private IReadOnlyList<BitmapSource> _photos = Array.Empty<BitmapSource>();
    private readonly IReadOnlyList<string> _sources;
    private SessionSnapshot _session;
    private readonly Func<BoothSettings> _getSettings;
    private readonly Action<BoothSettings> _saveSettings;
    private SessionDelivery? _delivery;
    private readonly List<EditorSticker> _stickers = new();
    private readonly List<FrameworkElement> _handles = new();
    private readonly StaRenderWorker _renderer = new("VHU review preview");
    private readonly CancellationTokenSource _lifetime;
    private Task _layoutTask = Task.CompletedTask;
    private int _layoutGeneration;
    private int _revision;
    private bool _loaded;
    private bool _busy;
    private bool _closeWhenIdle;

    public ReviewWindow(IReadOnlyList<string> sources, SessionSnapshot session,
        Func<BoothSettings> getSettings, Action<BoothSettings> saveSettings, CancellationToken parentToken)
    {
        _sources = sources; _session = session; _getSettings = getSettings; _saveSettings = saveSettings;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        InitializeComponent();
        LayoutCombo.ItemsSource = new[] { "Lưới 2×2", "Dải dọc — 1 cột × 4 hàng" };
        LayoutCombo.SelectedIndex = (int)session.Layout;
        Tools.IsEnabled = false;
        LoadLibrary();
        Closed += async (_, _) => { _lifetime.Cancel(); await _renderer.DisposeAsync(); _lifetime.Dispose(); };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _layoutTask = PrepareAsync(); await _layoutTask;
    }
    private async Task PrepareAsync()
    {
        Status.Text = "Đã nhận đủ 4 ảnh. Đang chuẩn hóa ảnh và dựng bản xem lại...";
        try
        {
            _photos = await _renderer.InvokeAsync(() => CompositeRenderer.PreparePhotos(_sources, _session, _lifetime.Token), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            foreach (var photo in _photos)
            {
                var grid = new Grid();
                grid.Children.Add(new Image { Source = photo, Stretch = Stretch.Uniform });
                grid.Children.Add(new Image { Source = _session.SingleFrame, Stretch = Stretch.Uniform, IsHitTestVisible = false });
                Thumbnails.Children.Add(new Border { Child = grid, Margin = new Thickness(4), Background = Brushes.White });
            }
            _delivery = new SessionDelivery(_photos, _getSettings, _saveSettings);
            _loaded = true; Tools.IsEnabled = true;
            await RefreshLayoutAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_lifetime.IsCancellationRequested) Status.Text = "Không chuẩn bị được ảnh: " + ex.Message + " Ảnh nguồn vẫn được giữ trong Working."; }
    }
    private async void Layout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _busy) return;
        _session = _session with { Layout = (PhotoLayout)LayoutCombo.SelectedIndex };
        _revision++;
        _layoutTask = RefreshLayoutAsync(); await _layoutTask;
    }

    private async Task RefreshLayoutAsync()
    {
        var generation = ++_layoutGeneration;
        var session = _session;
        ConfirmButton.IsEnabled = false; StickerCanvas.IsHitTestVisible = false;
        Status.Text = "Đang dựng bố cục...";
        try
        {
            var preview = await _renderer.InvokeAsync(() => CompositeRenderer.Compose(_photos, session, Array.Empty<StickerPlacement>(), true), _lifetime.Token);
            if (_lifetime.IsCancellationRequested || generation != _layoutGeneration) return;
            Scene.Width = StickerCanvas.Width = CompositeImage.Width = preview.PixelWidth;
            Scene.Height = StickerCanvas.Height = CompositeImage.Height = preview.PixelHeight;
            CompositeImage.Source = preview;
            RebuildStickerControls(); ConfirmButton.IsEnabled = true; StickerCanvas.IsHitTestVisible = true;
            Status.Text = "Chưa xuất thay đổi • Bố cục và sticker sẵn sàng để xác nhận.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_lifetime.IsCancellationRequested && generation == _layoutGeneration) Status.Text = ex.Message; }
    }

    private void LoadLibrary()
    {
        Library.Children.Clear();
        foreach (var file in Directory.EnumerateFiles(DefaultAssets.StickerDirectory, "*.png").OrderBy(path => path))
        {
            try
            {
                var image = BitmapTools.Load(file);
                var button = new Button { Content = new Image { Source = image, Width = 52, Height = 52, Stretch = Stretch.Uniform }, Margin = new Thickness(4) };
                button.Click += (_, _) => { if (_busy) return; _stickers.Add(new EditorSticker(image)); _revision++; RebuildStickerControls(); };
                Library.Children.Add(button);
            }
            catch (Exception ex) { Status.Text = "Không đọc được sticker: " + ex.Message; }
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new OpenFileDialog { Filter = "PNG|*.png", Multiselect = true };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            foreach (var path in picker.FileNames)
            {
                var image = BitmapTools.Load(path);
                File.Copy(path, Path.Combine(DefaultAssets.StickerDirectory, Guid.NewGuid().ToString("N") + ".png"));
                _stickers.Add(new EditorSticker(image)); _revision++;
            }
            LoadLibrary(); RebuildStickerControls();
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _stickers.Clear(); _revision++; RebuildStickerControls();
    }

    private (double Width, double Height) StickerSize(EditorSticker sticker)
    {
        var aspect = sticker.Image.PixelWidth / (double)sticker.Image.PixelHeight;
        // Preserve X/Y in 0..1 across layout switches, keep the PNG's intrinsic aspect,
        // and temporarily fit near-edge stickers without mutating their desired width.
        var width = Math.Min(sticker.Width * Scene.Width,
            Math.Min((1 - sticker.X) * Scene.Width, (1 - sticker.Y) * Scene.Height * aspect));
        width = Math.Max(0.01, width);
        return (width, width / aspect);
    }

    private void RebuildStickerControls()
    {
        StickerCanvas.Children.Clear(); _handles.Clear();
        foreach (var sticker in _stickers)
        {
            var (width, height) = StickerSize(sticker);
            var control = new Grid { Width = width, Height = height };
            var image = new Image { Source = sticker.Image, Stretch = Stretch.Fill, Cursor = Cursors.SizeAll };
            control.Children.Add(image);
            Canvas.SetLeft(control, sticker.X * Scene.Width); Canvas.SetTop(control, sticker.Y * Scene.Height);
            Point last = default;
            image.MouseLeftButtonDown += (_, ev) => { if (_busy) return; last = ev.GetPosition(StickerCanvas); image.CaptureMouse(); ev.Handled = true; };
            image.MouseMove += (_, ev) =>
            {
                if (_busy || !image.IsMouseCaptured || ev.LeftButton != MouseButtonState.Pressed) return;
                var current = ev.GetPosition(StickerCanvas);
                var left = Math.Clamp(Canvas.GetLeft(control) + current.X - last.X, 0, Math.Max(0, Scene.Width - control.Width));
                var top = Math.Clamp(Canvas.GetTop(control) + current.Y - last.Y, 0, Math.Max(0, Scene.Height - control.Height));
                Canvas.SetLeft(control, left); Canvas.SetTop(control, top);
                sticker.X = left / Scene.Width; sticker.Y = top / Scene.Height;
                sticker.Width = control.Width / Scene.Width; last = current; _revision++;
            };
            image.MouseLeftButtonUp += (_, _) => image.ReleaseMouseCapture();
            var thumb = new Thumb { Width = 24, Height = 24, Background = Brushes.DodgerBlue, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            thumb.DragDelta += (_, ev) =>
            {
                if (_busy) return;
                var aspect = sticker.Image.PixelWidth / (double)sticker.Image.PixelHeight;
                var maximum = Math.Min((1 - sticker.X) * Scene.Width, (1 - sticker.Y) * Scene.Height * aspect);
                control.Width = Math.Clamp(control.Width + (ev.HorizontalChange + ev.VerticalChange * aspect) / 2, Math.Min(36, maximum), maximum);
                control.Height = control.Width / aspect; sticker.Width = control.Width / Scene.Width; _revision++;
            };
            var remove = new Button { Content = "×", Width = 28, Height = 28, FontSize = 20, Margin = new Thickness(0), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            remove.Click += (_, _) => { if (_busy) return; _stickers.Remove(sticker); _revision++; RebuildStickerControls(); };
            control.Children.Add(thumb); control.Children.Add(remove); StickerCanvas.Children.Add(control);
            _handles.Add(thumb); _handles.Add(remove);
        }
        UpdateHandleSizes();
    }

    private void SceneViewbox_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHandleSizes();
    private void UpdateHandleSizes()
    {
        if (SceneViewbox is null || Scene is null || SceneViewbox.ActualWidth <= 0 || SceneViewbox.ActualHeight <= 0) return;
        var scale = Math.Min(SceneViewbox.ActualWidth / Scene.Width, SceneViewbox.ActualHeight / Scene.Height);
        if (scale <= 0) return;
        // Controls stay about 24 screen pixels, including the much taller vertical-strip canvas.
        foreach (var handle in _handles)
        {
            handle.Width = handle.Height = 24 / scale;
            if (handle is Button button) button.FontSize = 18 / scale;
        }
    }

    private StickerPlacement[] Snapshot() => _stickers.Select(sticker =>
    {
        var (width, height) = StickerSize(sticker);
        return new StickerPlacement(sticker.Image, sticker.X, sticker.Y, width / Scene.Width, height / Scene.Height);
    }).ToArray();

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _delivery is null || !_layoutTask.IsCompletedSuccessfully) return;
        _busy = true; Tools.IsEnabled = false; StickerCanvas.IsHitTestVisible = false; ConfirmButton.IsEnabled = false;
        try
        {
            var success = await _delivery.DeliverAsync(_session, Snapshot(), _revision, this, _lifetime.Token);
            Status.Text = success ? "Đã có mã QR. Bạn có thể đóng cửa sổ để bắt đầu phiên mới." : "Ảnh và sticker vẫn được giữ. Có thể sửa và xác nhận lại.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally
        {
            _busy = false;
            if (_closeWhenIdle) Close();
            else { Tools.IsEnabled = true; StickerCanvas.IsHitTestVisible = true; ConfirmButton.IsEnabled = true; }
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_busy) { _lifetime.Cancel(); return; }
        e.Cancel = true; _closeWhenIdle = true; _lifetime.Cancel();
    }
}
