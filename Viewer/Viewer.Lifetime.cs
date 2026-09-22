using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private readonly Internal.ViewerLifetime _lifetime = new();
    private readonly TaskCompletionSource _windowStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    /// <summary>窗口关闭后触发；后台任务和线程退出请等待 DisposeAsync。</summary>
    public event EventHandler? Closed;

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            CleanupOnWindowClosed();
            _ = DisposeAsync();
            foreach (EventHandler handler in Closed?.GetInvocationList() ?? [])
                try { handler(this, EventArgs.Empty); } catch (Exception ex) { _logger.LogWarning(ex, "Closed handler failed"); }
        }
        finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
    }

    private void CleanupOnWindowClosed()
    {
        Submission? pending;
        FrameLease? current;
        lock (_frameGate)
        {
            if (_closed) return;
            _lifetime.BeginDisposal();
            _closed = true;
            pending = _pending; _pending = null;
            current = _currentFrame; _currentFrame = null;
        }
        _shutdown.Cancel();
        if (pending != null) Finish(pending, FrameSubmitStatus.Closed);

        Cleanup(() => _interaction?.Dispose());
        Cleanup(() => _pixelInfoOverlay?.Disable());
        Cleanup(() => _measureManager?.Dispose());
        Cleanup(() => _window.Layers.Close());
        Cleanup(CloseHud);
        ReleaseFrame(current);
        ReleaseFrame(_menuSnapshot?.Frame); _menuSnapshot = null;
        Cleanup(_presenter.Dispose);
        Cleanup(() => { _d3dPresenter?.Dispose(); _d3dPresenter = null; });
        FrameCommitted = null;
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
            if (!_closed && !_window.Dispatcher.HasShutdownStarted)
            {
                try
                {
                    await _window.Dispatcher.InvokeAsync(() => _window.CloseProgrammatically()).Task.ConfigureAwait(false);
                }
                catch (TaskCanceledException) { }
            }
            Task render;
            lock (_frameGate) render = _renderTask;
            await Task.WhenAll(render, _measureManager?.Completion ?? Task.CompletedTask, _windowStopped.Task).ConfigureAwait(false);
            _lifetime.Complete();
            if (_window.ClosingError is { } error) _disposeCompletion.TrySetException(error);
            else _disposeCompletion.TrySetResult();
        }
        catch (Exception ex) { _disposeCompletion.TrySetException(ex); }
    }
    public virtual ValueTask DisposeAsync()
    {
        _lifetime.BeginDisposal();
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0) _ = CompleteDisposalAsync();
        GC.SuppressFinalize(this);
        return new ValueTask(_disposeCompletion.Task);
    }

    private T InvokeAlive<T>(Func<T> action) => _lifetime.Invoke(_window.Dispatcher, action);
    private void InvokeAlive(Action action) => InvokeAlive(() => { action(); return true; });
}
