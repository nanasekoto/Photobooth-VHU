using System.IO;
using System.Text.Json;
using Photobooth.App.Models;

namespace Photobooth.App.Services;

public static class SettingsStore
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoboothVHU");
    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static BoothSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        var settings = JsonSerializer.Deserialize<BoothSettings>(File.ReadAllText(SettingsPath)) ?? new();
        settings.JpegQuality = Math.Clamp(settings.JpegQuality, 1, 100);
        settings.SyncTimeoutSeconds = Math.Clamp(settings.SyncTimeoutSeconds, 30, 1800);
        if (settings.VideoDeviceIndex < 0) settings.VideoDeviceIndex = 0;
        if (!Enum.IsDefined(settings.PreviewSource)) settings.PreviewSource = LiveViewMode.VideoDevice;
        if (!Enum.IsDefined(settings.CaptureMode)) settings.CaptureMode = CaptureMode.Simulation;
        if (!Enum.IsDefined(settings.Layout)) settings.Layout = PhotoLayout.Grid2x2;
        return settings;
    }

    public static void Save(BoothSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);
    }
}
