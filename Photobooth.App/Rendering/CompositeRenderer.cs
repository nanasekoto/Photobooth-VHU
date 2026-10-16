using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.App.Imaging;
using Photobooth.App.Models;
using Photobooth.App.Services;

namespace Photobooth.App.Rendering;

public static class CompositeRenderer
{
    public const int PhotoCount = 4;
    public const int SingleWidth = 4200;
    public const int SingleHeight = 2800;
    // Canvas has no extra gutters/footer: every cell preserves the full 3:2 photo.
    public static (int Width, int Height) OutputSize(PhotoLayout layout) => layout == PhotoLayout.Grid2x2
        ? (8400, 5600) : (4200, 11200);

    public static BitmapSource[] PreparePhotos(IReadOnlyList<string> sources, SessionSnapshot session, CancellationToken token)
    {
        if (sources.Count != PhotoCount) throw new InvalidOperationException("Phiên phải có đúng 4 ảnh.");
        var result = new BitmapSource[PhotoCount];
        for (var i = 0; i < PhotoCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var source = BitmapTools.Load(sources[i]);
            // Downsample before the CPU colour pass. Each photo receives the colour transform exactly once.
            result[i] = ImageProcessor.Apply(BitmapTools.NormalizeLandscape(source, SingleWidth, SingleHeight), session.Filter, session.Lut);
        }
        return result;
    }

    public static BitmapSource Compose(IReadOnlyList<BitmapSource> photos, SessionSnapshot session,
        IReadOnlyList<StickerPlacement> stickers, bool preview = false)
    {
        if (photos.Count != PhotoCount) throw new InvalidOperationException("Cần đủ 4 ảnh để ghép.");
        BitmapTools.ValidateFrame(session.LayoutFrame, session.Layout == PhotoLayout.Grid2x2 ? 1.5 : 0.375, "Frame bố cục");
        var (width, height) = OutputSize(session.Layout);
        if (preview) { height = (int)Math.Round(height * (1200d / width)); width = 1200; }
        return BitmapTools.Render(width, height, dc =>
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var columns = session.Layout == PhotoLayout.Grid2x2 ? 2 : 1;
            var rows = PhotoCount / columns;
            var cellWidth = width / (double)columns;
            var cellHeight = height / (double)rows;
            for (var i = 0; i < PhotoCount; i++)
                dc.DrawImage(photos[i], new Rect(i % columns * cellWidth, i / columns * cellHeight, cellWidth, cellHeight));
            dc.DrawImage(session.LayoutFrame, new Rect(0, 0, width, height));
            foreach (var sticker in stickers)
                dc.DrawImage(sticker.Image, new Rect(sticker.X * width, sticker.Y * height, sticker.Width * width, sticker.Height * height));
        });
    }

    public static string Export(IReadOnlyList<BitmapSource> photos, SessionSnapshot session,
        IReadOnlyList<StickerPlacement> stickers, string directory, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        BitmapTools.ValidateFrame(session.SingleFrame, 1.5, "Frame ảnh đơn");
        for (var i = 0; i < PhotoCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var index = i;
            var single = BitmapTools.Render(SingleWidth, SingleHeight, dc =>
            {
                dc.DrawImage(photos[index], new Rect(0, 0, SingleWidth, SingleHeight));
                dc.DrawImage(session.SingleFrame, new Rect(0, 0, SingleWidth, SingleHeight));
            });
            SaveAtomic(single, Path.Combine(directory, $"photo_{i + 1:00}.jpg"), session.JpegQuality);
        }
        token.ThrowIfCancellationRequested();
        SaveAtomic(Compose(photos, session, stickers), Path.Combine(directory, "photobooth_vhu.jpg"), session.JpegQuality);
        return directory;
    }

    private static void SaveAtomic(BitmapSource bitmap, string path, int quality)
    {
        // Rename only after encoding completes, so Drive never sees a partially written JPG.
        var temporary = path + ".partial";
        try { ImageExporter.SaveJpeg(bitmap, temporary, quality); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
