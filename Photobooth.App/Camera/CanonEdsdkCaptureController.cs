using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static Photobooth.App.Camera.CanonNative;

namespace Photobooth.App.Camera;

public sealed class CanonEdsdkCaptureController : IPhotoCaptureController
{
    private readonly TaskCompletionSource<Dispatcher> _dispatcherReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ObjectHandler _objectHandler;
    private readonly StateHandler _stateHandler;
    private IntPtr _camera;
    private bool _initialized;
    private bool _sessionOpen;
    private bool _faulted;
    private TaskCompletionSource? _pending;
    private string? _target;
    private DispatcherTimer? _events;
    private bool _disposed;

    public CanonEdsdkCaptureController()
    {
        _objectHandler = OnObject;
        _stateHandler = OnState;
        var thread = new Thread(() =>
        {
            _dispatcherReady.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Canon EDSDK STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private async Task InvokeAsync(Action action, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var dispatcher = await _dispatcherReady.Task.WaitAsync(token);
        await dispatcher.InvokeAsync(action, DispatcherPriority.Normal, token).Task;
    }

    public Task ConnectAsync(CancellationToken token) => InvokeAsync(() =>
    {
        if (_camera != IntPtr.Zero) return;
        try
        {
            Check(EdsInitializeSDK(), "khởi tạo SDK"); _initialized = true;
            Check(EdsGetCameraList(out var list), "tìm máy ảnh");
            try
            {
                Check(EdsGetChildCount(list, out var count), "đếm máy ảnh");
                if (count != 1) throw new InvalidOperationException("Hãy kết nối đúng một Canon qua USB và tắt EOS Utility.");
                Check(EdsGetChildAtIndex(list, 0, out _camera), "chọn máy ảnh");
            }
            finally { EdsRelease(list); }
            Check(EdsOpenSession(_camera), "mở phiên USB"); _sessionOpen = true;
            Check(EdsSetObjectEventHandler(_camera, 0x200, _objectHandler, IntPtr.Zero), "đăng ký sự kiện ảnh");
            Check(EdsSetCameraStateEventHandler(_camera, 0x300, _stateHandler, IntPtr.Zero), "đăng ký trạng thái");
            uint saveToHost = 2;
            Check(EdsSetPropertyData(_camera, 0x0000000B, 0, 4, ref saveToHost), "chọn lưu về máy tính");
            Check(EdsSetCapacity(_camera, new Capacity { FreeClusters = int.MaxValue, BytesPerSector = 4096, Reset = 1 }), "khai báo dung lượng host");
            _faulted = false;
            _events = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (_, _) =>
            {
                var error = EdsGetEvent();
                if (error != 0) Fail(new IOException($"Mất kết nối EDSDK (0x{error:X8}). Hãy kết nối lại."));
            }, Dispatcher.CurrentDispatcher);
            _events.Start();
        }
        catch (DllNotFoundException ex) { CloseNative(); throw new InvalidOperationException("Thiếu Canon EDSDK x64. Xem SETUP-VHU.md để cài DLL chính thức.", ex); }
        catch { CloseNative(); throw; }
    }, token);

    public Task AutoFocusAsync(CancellationToken token) => InvokeAsync(() =>
    {
        EnsureReady();
        Check(EdsSendCommand(_camera, 4, 1), "nhấn nửa cò AF");
    }, token);

    public async Task CaptureToFileAsync(string path, CancellationToken token)
    {
        Task? transfer = null;
        await InvokeAsync(() =>
        {
            EnsureReady();
            if (_pending is not null) throw new InvalidOperationException("Canon đang truyền một ảnh khác.");
            _target = path;
            _pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            transfer = _pending.Task;
            try { Check(EdsSendCommand(_camera, 4, 3), "nhấn hết cò chụp"); }
            catch { _pending = null; _target = null; throw; }
            finally { EdsSendCommand(_camera, 4, 0); }
        }, token);
        try { await transfer!.WaitAsync(TimeSpan.FromSeconds(45), token); }
        catch
        {
            await InvokeAsync(() => Fail(new IOException("Chụp/truyền ảnh bị gián đoạn. Kết nối lại Canon trước khi chụp tiếp.")));
            throw;
        }
    }

    private void EnsureReady()
    {
        if (_camera == IntPtr.Zero || _faulted) throw new InvalidOperationException("Canon chưa sẵn sàng. Hãy kết nối lại.");
    }
    private void Fail(Exception ex) { _faulted = true; _pending?.TrySetException(ex); _pending = null; _target = null; }

    private uint OnState(uint eventId, uint parameter, IntPtr context)
    {
        try { if (eventId == 0x301) Fail(new IOException("Canon đã ngắt kết nối hoặc tắt nguồn.")); }
        catch { /* Never let managed exceptions escape a native callback. */ }
        return 0;
    }

    private uint OnObject(uint eventId, IntPtr item, IntPtr context)
    {
        if (item == IntPtr.Zero) return 0;
        var transferCompleted = false;
        try
        {
            if (eventId != 0x208) return 0; // kEdsObjectEvent_DirItemRequestTransfer
            Check(EdsGetDirectoryItemInfo(item, out var info), "đọc thông tin JPEG");
            if (_pending is null || _target is null || !info.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (info.Size == 0 || info.Size > 100_000_000) throw new InvalidDataException("Dung lượng ảnh Canon không hợp lệ.");
            Check(EdsCreateMemoryStream(info.Size, out var stream), "tạo bộ đệm tải ảnh");
            try
            {
                Check(EdsDownload(item, info.Size, stream), "tải JPEG");
                Check(EdsDownloadComplete(item), "hoàn tất truyền JPEG"); transferCompleted = true;
                Check(EdsGetPointer(stream, out var pointer), "đọc dữ liệu JPEG");
                var bytes = new byte[checked((int)info.Size)];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                File.WriteAllBytes(_target, bytes);
                _pending.TrySetResult(); _pending = null; _target = null;
            }
            finally { EdsRelease(stream); }
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            if (eventId == 0x208 && !transferCompleted) EdsDownloadCancel(item);
            EdsRelease(item);
        }
        return 0;
    }

    private void CloseNative()
    {
        _events?.Stop();
        if (_camera != IntPtr.Zero)
        {
            EdsSendCommand(_camera, 4, 0);
            if (_sessionOpen) EdsCloseSession(_camera);
            EdsRelease(_camera); _camera = IntPtr.Zero;
        }
        _sessionOpen = false;
        if (_initialized) { EdsTerminateSDK(); _initialized = false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await InvokeAsync(() => { _pending?.TrySetCanceled(); _pending = null; CloseNative(); });
        _disposed = true;
        var dispatcher = await _dispatcherReady.Task;
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        GC.KeepAlive(_objectHandler); GC.KeepAlive(_stateHandler);
    }
}
