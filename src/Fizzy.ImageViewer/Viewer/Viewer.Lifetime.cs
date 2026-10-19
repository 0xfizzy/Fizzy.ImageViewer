using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc />
    public Task WhenStopping => _host.Control.Runtime.Lifetime.WhenStopping;

    /// <inheritdoc />
    public event EventHandler? Closed
    {
        add { lock (_host.Control.Runtime.Lifetime.Gate) { _host.Control.Runtime.Lifetime.ThrowIfStopping(); _closed += value; } }
        remove { lock (_host.Control.Runtime.Lifetime.Gate) _closed -= value; }
    }
    private EventHandler? _closed;
    internal void NotifyClosed(EventArgs e)
    {
        EventHandler? handlers;
        lock (_host.Control.Runtime.Lifetime.Gate)
        {
            handlers = _closed;
            _closed = null;
        }
        foreach (EventHandler handler in handlers?.GetInvocationList() ?? [])
            try { handler(this, e); } catch (Exception ex) { _logger.LogWarning(ex, "Closed handler failed"); }
    }
    internal void ClearNotifications()
    {
        // A failed control constructor cannot have exposed the Viewer to subscribers.
        if (_host.Control is not { } control) return;
        lock (control.Runtime.Lifetime.Gate)
        {
            _frameCommitted = null;
            _displayFailed = null;
            _measurementCompleted = null;
            _measurementRemoved = null;
            _measurementChanged = null;
        }
    }
    /// <summary>Stops admission, drains accepted frame/query work and waits for the STA to exit.</summary>
    /// <remarks>All callers observe the same completion, including owned-resource cleanup failures.
    /// A failed close does not imply that an unsafe native resource was released; it may remain isolated.
    /// Subscriber notification failures are logged separately and do not fault this result.</remarks>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _host.BeginDisposal();
    }
    private T InvokeAlive<T>(Func<T> action) => _host.Control.Runtime.Lifetime.Invoke(_host.Window.Dispatcher, action);
    private void InvokeAlive(Action action) => InvokeAlive(() => { action(); return true; });
}
