using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.App.Models;
using Photobooth.App.Services;

namespace Photobooth.App.Rendering;

public static class DefaultAssets
{
    public static string StickerDirectory => Path.Combine(SettingsStore.DataDirectory, "Stickers");
    public static void EnsureCreated(BoothSettings settings)
    {
        var frames = Path.Combine(SettingsStore.DataDirectory, "Frames");
        Directory.CreateDirectory(frames);
        Directory.CreateDirectory(StickerDirectory);
        settings.SingleFramePath = DefaultFrame(settings.SingleFramePath, Path.Combine(frames, "vhu-single.png"), 600, 400);
        settings.GridFramePath = DefaultFrame(settings.GridFramePath, Path.Combine(frames, "vhu-grid.png"), 1200, 800);
        settings.StripFramePath = DefaultFrame(settings.StripFramePath, Path.Combine(frames, "vhu-strip.png"), 600, 1600);
        SaveIfMissing(Path.Combine(StickerDirectory, "vhu.png"), () => BitmapTools.Render(300, 120, dc =>
        {
            dc.DrawRoundedRectangle(Brushes.DodgerBlue, null, new Rect(3, 3, 294, 114), 30, 30);
            Text(dc, "VHU", 68, Brushes.White, new Point(68, 16));
        }));
        SaveIfMissing(Path.Combine(StickerDirectory, "star.png"), () => BitmapTools.Render(200, 200, dc =>
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var i = 0; i < 10; i++)
                {
                    var angle = -Math.PI / 2 + i * Math.PI / 5;
                    var radius = i % 2 == 0 ? 92 : 42;
                    var point = new Point(100 + radius * Math.Cos(angle), 100 + radius * Math.Sin(angle));
                    if (i == 0) context.BeginFigure(point, true, true); else context.LineTo(point, true, false);
                }
            }
            dc.DrawGeometry(Brushes.Gold, new Pen(Brushes.White, 5), geometry);
        }));
        SaveIfMissing(Path.Combine(StickerDirectory, "heart.png"), () => BitmapTools.Render(200, 200, dc =>
        {
            var heart = Geometry.Parse("M100,180 C-45,80 25,-20 100,55 C175,-20 245,80 100,180 Z");
            dc.DrawGeometry(Brushes.DeepPink, new Pen(Brushes.White, 5), heart);
        }));
    }

    private static string DefaultFrame(string configured, string fallback, int width, int height)
    {
        // A missing operator-selected file is reported when loaded; never silently replace it.
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        SaveIfMissing(fallback, () => BitmapTools.Render(width, height, dc =>
        {
            dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 12), new Rect(6, 6, width - 12, height - 12));
            dc.DrawRoundedRectangle(Brushes.DodgerBlue, null, new Rect(12, 12, 140, 40), 8, 8);
            Text(dc, "VHU • PHOTO", 16, Brushes.White, new Point(23, 21));
        }));
        return fallback;
    }

    private static void Text(DrawingContext dc, string value, double size, Brush brush, Point point) =>
        dc.DrawText(new FormattedText(value, CultureInfo.GetCultureInfo("vi-VN"), FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, brush, 1), point);

    private static void SaveIfMissing(string path, Func<BitmapSource> create)
    {
        if (File.Exists(path)) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(create()));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
