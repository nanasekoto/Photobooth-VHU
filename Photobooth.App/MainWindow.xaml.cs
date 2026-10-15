using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Photobooth.App.Camera;
using Photobooth.App.Imaging;
using Photobooth.App.Services;

namespace Photobooth.App;

public partial class MainWindow : Window
{
    private readonly ICameraController _camera = new WebcamCameraController();
    private readonly List<BitmapSource> _capturedShots = new();
    private readonly DispatcherTimer _liveViewTimer = new();

    private BitmapSource? _rawPreview;
    private CubeLut? _activeLut;
    private bool _isBusy;
    private string _outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MiraiPhotobooth");

    public MainWindow()
    {
        InitializeComponent();
        OutputFolderText.Text = _outputDirectory;

        _liveViewTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 FPS
        _liveViewTimer.Tick += async (s, e) =>
        {
            if (!_isBusy && _camera.IsConnected)
            {
                _rawPreview = await _camera.GetLiveViewAsync();
                RefreshPreview();
            }
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetStatus("Sẵn sàng. Hãy bấm 'Bật Webcam' để xem trực tiếp.");
    }

    private async void ConnectCamera_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Đang mở webcam...");
            await _camera.ConnectAsync();
            ConnectionStatus.Text = "WEBCAM ĐANG CHẠY";
            ConnectionDot.Fill = Brushes.LightGreen;
            ConnectButton.Content = "Đã kết nối";
            ConnectButton.IsEnabled = false;
            SetStatus("Kết nối webcam thành công.");

            _liveViewTimer.Start();
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi kết nối webcam: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rawPreview is not null) RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_rawPreview is null) return;
        try
        {
            var preset = GetSelectedPreset();
            if (preset == FilterPreset.CustomLut && _activeLut is null)
            {
                PreviewImage.Source = _rawPreview;
                return;
            }
            PreviewImage.Source = ImageProcessor.Apply(_rawPreview, preset, _activeLut);
        }
        catch (Exception ex)
        {
            SetStatus($"Không áp dụng được bộ lọc: {ex.Message}");
        }
    }

    private FilterPreset GetSelectedPreset()
    {
        if (FilterComboBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<FilterPreset>(item.Tag?.ToString(), out var preset)) return preset;
        return FilterPreset.Original;
    }

    #region Khung Viền PNG
    private void LoadFramePng_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn Khung viền PNG (nền trong suốt)",
            Filter = "Ảnh PNG (*.png)|*.png|Tất cả tệp (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var bitmap = new BitmapImage(new Uri(dialog.FileName));
                FramePngOverlay.Source = bitmap;
                FrameStatusText.Text = $"Khung: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Không thể mở khung ảnh: {ex.Message}", "Lỗi ảnh", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ClearFrame_Click(object sender, RoutedEventArgs e)
    {
        FramePngOverlay.Source = null;
        FrameStatusText.Text = "Đã gỡ bỏ khung ảnh.";
    }
    #endregion

    #region Sticker PNG & Emoji (Kéo Thả)
    private void AddCustomStickerPng_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn Sticker PNG",
            Filter = "Ảnh PNG (*.png)|*.png|Tất cả tệp (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(file)),
                    Width = 100,
                    Height = 100,
                    Stretch = Stretch.Uniform,
                    Cursor = Cursors.SizeAll
                };
                AttachDragEvent(img);
                Canvas.SetLeft(img, Math.Max(0, (StickerCanvas.ActualWidth / 2) - 50));
                Canvas.SetTop(img, Math.Max(0, (StickerCanvas.ActualHeight / 2) - 50));
                StickerCanvas.Children.Add(img);
            }
        }
    }

    private void AddEmojiSticker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string emoji)
        {
            var textBlock = new TextBlock
            {
                Text = emoji,
                FontSize = 54,
                Cursor = Cursors.SizeAll
            };
            AttachDragEvent(textBlock);
            Canvas.SetLeft(textBlock, Math.Max(0, (StickerCanvas.ActualWidth / 2) - 30));
            Canvas.SetTop(textBlock, Math.Max(0, (StickerCanvas.ActualHeight / 2) - 30));
            StickerCanvas.Children.Add(textBlock);
        }
    }

    private void AttachDragEvent(UIElement element)
    {
        bool isDragging = false;
        Point clickPosition = new Point();

        element.MouseLeftButtonDown += (s, ev) =>
        {
            isDragging = true;
            clickPosition = ev.GetPosition(StickerCanvas);
            element.CaptureMouse();
            ev.Handled = true;
        };

        element.MouseMove += (s, ev) =>
        {
            if (isDragging)
            {
                Point currentPos = ev.GetPosition(StickerCanvas);
                double left = Canvas.GetLeft(element) + (currentPos.X - clickPosition.X);
                double top = Canvas.GetTop(element) + (currentPos.Y - clickPosition.Y);
                Canvas.SetLeft(element, left);
                Canvas.SetTop(element, top);
                clickPosition = currentPos;
            }
        };

        element.MouseLeftButtonUp += (s, ev) =>
        {
            isDragging = false;
            element.ReleaseMouseCapture();
        };

        // Nhấp đúp chuột vào sticker để xóa
        element.MouseRightButtonDown += (s, ev) =>
        {
            StickerCanvas.Children.Remove(element);
            ev.Handled = true;
        };
    }

    private void ClearStickers_Click(object sender, RoutedEventArgs e)
    {
        StickerCanvas.Children.Clear();
    }
    #endregion

    private void LoadLut_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn 3D LUT",
            Filter = "3D LUT (*.cube)|*.cube|Tất cả tệp (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _activeLut = CubeLut.Load(dialog.FileName);
            LutStatus.Text = $"Đã nạp: {Path.GetFileName(dialog.FileName)}";
            var lutItem = FilterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "CustomLut");
            if (lutItem is not null) FilterComboBox.SelectedItem = lutItem;
            RefreshPreview();
            SetStatus("Đã nạp LUT thành công.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không đọc được LUT", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StartSession_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        if (!_camera.IsConnected)
        {
            MessageBox.Show(this, "Hãy bấm bật webcam trước.", "Chưa kết nối", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isBusy = true;
        StartSessionButton.IsEnabled = false;
        _capturedShots.Clear();
        SessionProgressText.Text = "Chuẩn bị chụp...";
        var sessionDirectory = Path.Combine(_outputDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(sessionDirectory);
        var preset = GetSelectedPreset();

        try
        {
            for (var shotIndex = 1; shotIndex <= 4; shotIndex++)
            {
                CountdownOverlay.Visibility = Visibility.Visible;
                for (var seconds = 3; seconds >= 1; seconds--)
                {
                    CountdownText.Text = seconds.ToString();
                    SessionProgressText.Text = $"Ảnh {shotIndex}/4 · Chụp sau {seconds} giây";
                    await Task.Delay(1000);
                }
                CountdownOverlay.Visibility = Visibility.Collapsed;
                SessionProgressText.Text = $"Đang xử lý ảnh {shotIndex}/4…";

                var captured = await _camera.CaptureAsync();
                var processed = ImageProcessor.Apply(captured, preset, _activeLut);
                _capturedShots.Add(processed);
                ImageExporter.SaveJpeg(processed, Path.Combine(sessionDirectory, $"photo_{shotIndex:02}.jpg"), 95);
                SetStatus($"Đã lưu ảnh {shotIndex}/4.");
            }

            SessionProgressText.Text = "Đang ghép dải ảnh…";
            var strip = ImageExporter.ComposeStrip(_capturedShots, "Classic");
            var stripPath = Path.Combine(sessionDirectory, "photostrip.jpg");
            ImageExporter.SaveJpeg(strip, stripPath, 95);

            _liveViewTimer.Stop();
            PreviewImage.Source = strip;

            SessionProgressText.Text = "Hoàn tất · 4 ảnh riêng + 1 dải ảnh";
            SetStatus($"Đã xuất ảnh tại: {sessionDirectory}");

            MessageBox.Show(this,
                $"Hoàn tất phiên chụp.\n\nĐã lưu 4 ảnh riêng và 1 dải ảnh tại:\n{sessionDirectory}",
                "Xuất ảnh thành công", MessageBoxButton.OK, MessageBoxImage.Information);

            _liveViewTimer.Start();
        }
        catch (Exception ex)
        {
            SetStatus($"Phiên chụp lỗi: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Lỗi phiên chụp", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CountdownOverlay.Visibility = Visibility.Collapsed;
            StartSessionButton.IsEnabled = true;
            _isBusy = false;
        }
    }

    private void ChooseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Chọn thư mục xuất ảnh", Multiselect = false };
        if (dialog.ShowDialog() == true)
        {
            _outputDirectory = dialog.FolderName;
            OutputFolderText.Text = _outputDirectory;
            SetStatus($"Thư mục xuất đã đổi: {_outputDirectory}");
        }
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_outputDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _outputDirectory,
            UseShellExecute = true
        });
    }

    private void SetStatus(string message) => FooterStatus.Text = message;

    protected override async void OnClosed(EventArgs e)
    {
        _liveViewTimer.Stop();
        await _camera.DisposeAsync();
        base.OnClosed(e);
    }
}