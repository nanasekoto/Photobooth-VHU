using System.IO;
using Photobooth.App.Imaging;

namespace Photobooth.App.Models;

public enum PhotoLayout { Grid2x2, VerticalStrip }
public enum CaptureMode { Simulation, Canon }
public enum BoothState { Ready, Capturing, Decorating, Exporting }

public sealed class BoothSettings
{
    public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoboothVHU");
    public PhotoLayout Layout { get; set; } = PhotoLayout.Grid2x2;
    public FilterPreset Filter { get; set; } = FilterPreset.Original;
    public string LutPath { get; set; } = "";
    public string SingleFramePath { get; set; } = "";
    public string GridFramePath { get; set; } = "";
    public string StripFramePath { get; set; } = "";
    public CaptureMode CaptureMode { get; set; } = CaptureMode.Simulation;
    public int VideoDeviceIndex { get; set; }
    public int VideoWidth { get; set; } = 1920;
    public int VideoHeight { get; set; } = 1080;
    public int JpegQuality { get; set; } = 90;
    public string DriveParentId { get; set; } = "";
    public string GoogleCredentialsPath { get; set; } = "";
    public int SyncTimeoutSeconds { get; set; } = 180;
}

public sealed record SessionSnapshot(PhotoLayout Layout, FilterPreset Filter, CubeLut? Lut,
    System.Windows.Media.Imaging.BitmapSource SingleFrame,
    System.Windows.Media.Imaging.BitmapSource LayoutFrame,
    string OutputDirectory, int JpegQuality, string DriveParentId, string GoogleCredentialsPath, int SyncTimeoutSeconds);

public sealed record StickerPlacement(System.Windows.Media.Imaging.BitmapSource Image,
    double X, double Y, double Width, double Height);
