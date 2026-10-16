using System.Windows.Threading;

namespace Photobooth.App.Rendering;

/// <summary>A reusable STA dispatcher. Callers submit at most one preview at a time.</summary>
public sealed class StaRenderWorker : IAsyncDisposable
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public StaRenderWorker(string name)
    {
        var thread = new Thread(() =>
        {
            try { _ready.TrySetResult(Dispatcher.CurrentDispatcher); Dispatcher.Run(); }
            catch (Exception ex) { _ready.TrySetException(ex); }
            finally { _stopped.TrySetResult(); }
        }) { Name = name, IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public async Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var dispatcher = await _ready.Task.WaitAsync(token);
        return await dispatcher.InvokeAsync(() => { token.ThrowIfCancellationRequested(); return action(); }, DispatcherPriority.Normal, token).Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var dispatcher = await _ready.Task;
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        await _stopped.Task;
    }
}
