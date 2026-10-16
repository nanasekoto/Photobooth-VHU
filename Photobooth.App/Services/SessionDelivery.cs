using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Photobooth.App.Models;
using Photobooth.App.Rendering;
using Photobooth.App.Views;

namespace Photobooth.App.Services;

/// <summary>One delivery coordinator per captured session, retained across editor/QR retries.</summary>
public sealed class SessionDelivery(IReadOnlyList<BitmapSource> photos, Func<BoothSettings> getSettings, Action<BoothSettings> saveSettings)
{
    private readonly string _sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    private readonly Dictionary<string, string> _destinations = new(StringComparer.OrdinalIgnoreCase);
    private string? _exportDirectory;
    private int _exportedRevision = -1;

    public async Task<bool> DeliverAsync(SessionSnapshot session, StickerPlacement[] stickers, int revision, Window owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        await using var renderer = new StaRenderWorker("VHU final export");
        var popup = new SessionQrWindow { Owner = owner };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = false;
        var succeeded = false;
        Task pipeline = Task.CompletedTask;
        popup.Closed += (_, _) => { stop.Cancel(); closed.TrySetResult(); };
        using var registration = token.Register(() => popup.Dispatcher.BeginInvoke(new Action(() => { if (popup.IsVisible) popup.Close(); })));

        async Task RunAsync()
        {
            if (running || stop.IsCancellationRequested) return;
            running = true; succeeded = false; popup.Loading();
            try
            {
                var settings = getSettings();
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.OutputDirectory));
                if (!_destinations.TryGetValue(root, out var destination))
                {
                    Directory.CreateDirectory(root);
                    destination = Path.Combine(root, _sessionName);
                    if (Directory.Exists(destination))
                        throw new IOException("Thư mục phiên đã tồn tại ở đường dẫn này. Chọn thư mục cha khác để tránh ghi đè ảnh của phiên khác.");
                    Directory.CreateDirectory(destination);
                    _destinations.Add(root, destination);
                }
                if (!string.Equals(_exportDirectory, destination, StringComparison.OrdinalIgnoreCase) || _exportedRevision != revision)
                {
                    popup.Loading("Loading...\nĐang xuất 4 ảnh lẻ và 1 ảnh ghép...");
                    // Invalidate before exporting: a cancelled/partial rewrite must never be reused as complete.
                    _exportedRevision = -1;
                    _exportDirectory = destination;
                    await renderer.InvokeAsync(() => CompositeRenderer.Export(photos, session, stickers, destination, stop.Token), stop.Token);
                    _exportedRevision = revision;
                }
                var progress = new Progress<string>(text => { if (popup.IsVisible && !stop.IsCancellationRequested) popup.Progress(text); });
                using var drive = new DriveDesktopShareService();
                var url = await drive.WaitAndShareAsync(destination, DriveShareOptions.From(settings), progress, stop.Token);
                stop.Token.ThrowIfCancellationRequested();
                popup.Success(url); succeeded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (popup.IsVisible) popup.Error(ex.Message + "\n\nẢnh và trang trí vẫn được giữ. Vị trí lưu: " + (_exportDirectory ?? "Chưa xuất"));
            }
            finally { running = false; }
        }

        popup.RetryRequested += (_, _) => { if (!running) pipeline = RunAsync(); };
        popup.SettingsRequested += (_, _) =>
        {
            if (running) return;
            var dialog = new DriveSettingsWindow(getSettings(), _exportDirectory) { Owner = popup };
            if (dialog.ShowDialog() == true)
            {
                saveSettings(dialog.Settings);
                popup.Error("Đã lưu cấu hình Drive. Bấm Thử lại để tiếp tục đúng phiên này, không cần chụp hoặc trang trí lại.");
            }
        };
        owner.IsEnabled = false;
        try
        {
            popup.Show(); pipeline = RunAsync();
            await closed.Task; await pipeline;
            return succeeded;
        }
        finally
        {
            stop.Cancel();
            await renderer.DisposeAsync();
            if (popup.IsVisible) popup.Close();
            if (owner.IsVisible) { owner.IsEnabled = true; owner.Activate(); }
        }
    }

}
