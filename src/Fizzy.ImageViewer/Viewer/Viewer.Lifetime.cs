using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <summary>Raised after window closure; await DisposeAsync for background work and STA termination.</summary>
    public event EventHandler? Closed;
    internal void NotifyClosed(EventArgs e)
    {
        var handlers = Closed;
        Closed = null;
        foreach (EventHandler handler in handlers?.GetInvocationList() ?? [])
            try { handler(this, e); } catch (Exception ex) { _logger.LogWarning(ex, "Closed handler failed"); }
    }
    internal void ClearNotifications()
    {
        FrameCommitted = null;
        MeasurementCompleted = null;
        MeasurementRemoved = null;
        MeasurementChanged = null;
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
    private T InvokeAlive<T>(Func<T> action) => _host.Lifetime.Invoke(_host.Window.Dispatcher, action);
    private void InvokeAlive(Action action) => InvokeAlive(() => { action(); return true; });
}
