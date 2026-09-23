using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <summary>窗口关闭后触发；后台任务和线程退出请等待 DisposeAsync。</summary>
    public event EventHandler? Closed;
    internal void NotifyClosed(EventArgs e)
    {
        foreach (EventHandler handler in Closed?.GetInvocationList() ?? [])
            try { handler(this, e); } catch (Exception ex) { _logger.LogWarning(ex, "Closed handler failed"); }
    }
    internal void ClearNotifications()
    {
        FrameCommitted = null;
        MeasurementCompleted = null;
        MeasurementRemoved = null;
    }
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _host.BeginDisposal();
    }
    private T InvokeAlive<T>(Func<T> action) => _host.Lifetime.Invoke(_host.Window.Dispatcher, action);
    private void InvokeAlive(Action action) => InvokeAlive(() => { action(); return true; });
}
