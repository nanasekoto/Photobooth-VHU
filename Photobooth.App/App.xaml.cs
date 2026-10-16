using System.Windows;

namespace Photobooth.App;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, @"Local\PhotoboothVHU", out _ownsInstance);
        if (!_ownsInstance)
        {
            MessageBox.Show("Photobooth VHU đang mở. Hãy dùng cửa sổ hiện tại.", "Photobooth VHU");
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
