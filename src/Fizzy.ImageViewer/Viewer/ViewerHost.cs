using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

/// <summary>Owns the standalone window and STA; the control owns viewing resources.</summary>
internal sealed class ViewerHost(Viewer owner, ILogger logger, bool showWindow)
{
    private Thread _windowThread = null!;
    private ViewerWindow? _window;
    private ImageViewerControl? _control;
    private readonly TaskCompletionSource _windowStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _errors = new();
    private int _disposeRequested;
    private bool _closedNotified;
    internal ViewerWindow Window => _window!;
    internal ImageViewerControl Control => _control!;

    internal void Start(Rendering.ICpuImagePresenter? presenter, double left, double top,
        double width, double height, Action<Viewer>? initialize, Action<ViewerInitializationStage>? checkpoint)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _windowThread = new Thread(() =>
        {
            Exception? failure = null;
            try
            {
                _control = new ImageViewerControl(logger, presenter, checkpoint);
                _window = new ViewerWindow("Fizzy ImageViewer", _control);
                _window.Closed += OnWindowClosed;
                _control.FrameCommitted += owner.NotifyFrameCommitted;
                _control.MeasurementCompleted += (_, args) => owner.NotifyMeasurementCompleted(args);
                _control.MeasurementRemoved += (_, args) => owner.NotifyMeasurementRemoved(args);
                _control.MeasurementChanged += (_, args) => owner.NotifyMeasurementChanged(args);
                if (!double.IsNaN(left)) _window.Left = left;
                if (!double.IsNaN(top)) _window.Top = top;
                if (!double.IsNaN(width)) _window.Width = width;
                if (!double.IsNaN(height)) _window.Height = height;
                initialize?.Invoke(owner);
                if (showWindow) _window.Show();
                started.SetResult();
                Dispatcher.Run();
            }
            catch (Exception error)
            {
                failure = error;
                if (started.Task.IsCompletedSuccessfully) _errors.Enqueue(error);
            }
            finally
            {
                if (_window != null) _window.Closed -= OnWindowClosed;
                _control?.Runtime.StopOnUiThread();
                try { _window?.CloseProgrammatically(); }
                catch (Exception error) { _errors.Enqueue(error); }
                if (started.Task.IsCompletedSuccessfully) NotifyClosedOnce();
                else owner.ClearNotifications();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                _windowStopped.TrySetResult();
                _ = BeginDisposal();
                if (failure != null) started.TrySetException(failure);
            }
        }) { IsBackground = true };
        _windowThread.SetApartmentState(ApartmentState.STA);
        _windowThread.Start();
        try { started.Task.GetAwaiter().GetResult(); }
        catch (Exception startupError)
        {
            _windowThread.Join();
            try { _disposeCompletion.Task.GetAwaiter().GetResult(); }
            catch (Exception cleanupError) { throw new AggregateException("Viewer startup and cleanup failed.", startupError, cleanupError); }
            throw;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _control!.Runtime.StopOnUiThread();
        _ = BeginDisposal();
        NotifyClosedOnce();
    }

    private void NotifyClosedOnce()
    {
        if (_closedNotified) return;
        _closedNotified = true;
        void Notify()
        {
            owner.ClearNotifications();
            owner.NotifyClosed(EventArgs.Empty);
        }
        if (_control != null) _control.Runtime.AfterNotifications(Notify);
        else Notify();
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            if (!_windowStopped.Task.IsCompleted && _window != null && !_window.Dispatcher.HasShutdownStarted)
            {
                try { await _window.Dispatcher.InvokeAsync(_window.CloseProgrammatically).Task.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception error) { _errors.Enqueue(error); }
            }
            if (_control != null)
            {
                try { await _control.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { _errors.Enqueue(error); }
            }
            if (_window != null && !_window.Dispatcher.HasShutdownStarted)
                _window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            await _windowStopped.Task.ConfigureAwait(false);
            await Task.Run(_windowThread.Join).ConfigureAwait(false);
            if (_window?.ClosingError is { } closingError) _errors.Enqueue(closingError);
            var errors = _errors.ToArray();
            if (errors.Length == 1) _disposeCompletion.TrySetException(errors[0]);
            else if (errors.Length > 1) _disposeCompletion.TrySetException(new AggregateException("Viewer shutdown failed.", errors));
            else _disposeCompletion.TrySetResult();
        }
        catch (Exception error) { _disposeCompletion.TrySetException(error); }
    }

    internal ValueTask BeginDisposal()
    {
        _control?.Runtime.Lifetime.BeginDisposal();
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0) _ = CompleteDisposalAsync();
        return new(_disposeCompletion.Task);
    }
}
