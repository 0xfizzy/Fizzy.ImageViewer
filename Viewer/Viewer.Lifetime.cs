using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private readonly ViewerLifetime _lifetime = new();
    private readonly TaskCompletionSource _windowStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    /// <summary>窗口关闭后触发；后台任务和线程退出请等待 DisposeAsync。</summary>
    public event EventHandler? Closed;

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            CleanupOnWindowClosed();
            _ = BeginDisposal();
            foreach (EventHandler handler in Closed?.GetInvocationList() ?? [])
                try { handler(this, EventArgs.Empty); } catch (Exception ex) { _logger.LogWarning(ex, "Closed handler failed"); }
        }
        finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
    }

    private void CleanupOnWindowClosed()
    {
        if (_closed) return;
        _lifetime.BeginDisposal();
        _closed = true;
        Cleanup(() => _pipeline?.StopOnUiThread());
        Cleanup(() => _menuSession?.Dispose());

        Cleanup(() => _interaction?.Dispose());
        Cleanup(() => _pixelInfoOverlay?.Disable());
        Cleanup(() => _queryScheduler?.Dispose());
        Cleanup(() => _measureManager?.Dispose());
        Cleanup(() => _window?.Layers.Close());
        Cleanup(CloseHud);
        Cleanup(() => _presentation?.Dispose());
        FrameCommitted = null;
        MeasurementCompleted = null;
        MeasurementRemoved = null;
    }

    private void Cleanup(Action action)
    {
        try { action(); } catch (Exception ex) { _logger.LogWarning(ex, "Viewer cleanup failed"); }
    }

    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeRequested;
    private async Task CompleteDisposalAsync()
    {
        try
        {
            if (!_closed && _window != null && !_window.Dispatcher.HasShutdownStarted)
            {
                try
                {
                    await _window.Dispatcher.InvokeAsync(() => _window.CloseProgrammatically()).Task.ConfigureAwait(false);
                }
                catch (TaskCanceledException) { }
            }
            await Task.WhenAll(_pipeline?.Completion ?? Task.CompletedTask, _queryScheduler?.Completion ?? Task.CompletedTask, _windowStopped.Task).ConfigureAwait(false);
            // The stop signal is set in the STA's finally block, just before it returns.
            // Join off-thread so DisposeAsync also guarantees actual thread termination.
            await Task.Run(_windowThread.Join).ConfigureAwait(false);
            _lifetime.Complete();
            if (_window?.ClosingError is { } error) _disposeCompletion.TrySetException(error);
            else _disposeCompletion.TrySetResult();
        }
        catch (Exception ex) { _disposeCompletion.TrySetException(ex); }
    }
    public virtual ValueTask DisposeAsync() => BeginDisposal();

    // Internal ownership must never dispatch into a partially constructed or failing subclass.
    private ValueTask BeginDisposal()
    {
        _lifetime.BeginDisposal();
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0) _ = CompleteDisposalAsync();
        GC.SuppressFinalize(this);
        return new ValueTask(_disposeCompletion.Task);
    }

    private T InvokeAlive<T>(Func<T> action) => _lifetime.Invoke(_window.Dispatcher, action);
    private void InvokeAlive(Action action) => InvokeAlive(() => { action(); return true; });
}
