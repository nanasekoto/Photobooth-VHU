using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.App.Services;

public static class ImageExporter
{
    public static void SaveJpeg(BitmapSource image, string path, int quality = 95)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static BitmapSource ComposeStrip(IReadOnlyList<BitmapSource> photos, string frameName)
    {
        if (photos.Count == 0) throw new ArgumentException("Cần ít nhất một ảnh để ghép.", nameof(photos));

        const int width = 1200;
        const int photoHeight = 1650;
        const int margin = 50;
        const int gap = 32;
        const int footer = 190;
        var height = margin + photos.Count * photoHeight + (photos.Count - 1) * gap + footer + margin;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = frameName switch
            {
                "Rose" => Color.FromRgb(255, 224, 233),
                "Midnight" => Color.FromRgb(32, 34, 45),
                _ => Color.FromRgb(250, 248, 243)
            };
            var foreground = frameName == "Midnight" ? Brushes.White : new SolidColorBrush(Color.FromRgb(45, 42, 48));
            dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, width, height));

            for (var i = 0; i < photos.Count; i++)
            {
                var y = margin + i * (photoHeight + gap);
                var rect = new Rect(margin, y, width - margin * 2, photoHeight);
                dc.DrawRectangle(Brushes.White, null, rect);
                dc.DrawImage(photos[i], rect);
                var border = frameName == "Midnight" ? Color.FromRgb(90, 92, 112) : Color.FromRgb(219, 215, 211);
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(border), 3), rect);
            }

            var title = new FormattedText("PHOTOBOOTH", CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 36, foreground, 1.0);
            var date = new FormattedText(DateTime.Now.ToString("dd.MM.yyyy  ·  HH:mm", CultureInfo.GetCultureInfo("vi-VN")),
                CultureInfo.GetCultureInfo("vi-VN"), FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 23, foreground, 1.0);
            dc.DrawText(title, new Point((width - title.Width) / 2, height - footer + 35));
            dc.DrawText(date, new Point((width - date.Width) / 2, height - footer + 92));
        }

        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
