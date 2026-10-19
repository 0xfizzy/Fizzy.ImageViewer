using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Serializes immutable notifications across all measurement handles and viewer events.</summary>
internal sealed class MeasurementNotificationQueue(ILogger logger)
{
    private readonly Queue<Action> _pending = [];
    private readonly Queue<Action> _afterNotifications = [];
    private int _depth;
    private bool _draining;

    internal IDisposable Defer()
    {
        _depth++;
        return new Scope(this);
    }

    internal void Post(Action notification)
    {
        _pending.Enqueue(notification);
        Drain();
    }

    // Shutdown waits for in-progress mutations as well as already queued events.
    // Cleanup callbacks may pump messages and close the window before their outer
    // measurement disposal has captured its terminal removal notification.
    internal void AfterNotifications(Action action)
    {
        _afterNotifications.Enqueue(action);
        Drain();
    }

    internal void Notify<T>(EventHandler<T>? handlers, object sender, T value)
    {
        if (handlers != null) PostNotification(handlers, sender, value);
    }

    // A multicast delegate is an immutable subscription snapshot. Queue one delivery
    // instead of a closure per subscriber; reentrant posts still follow every subscriber
    // of this notification because Drain remains active throughout this callback.
    private void PostNotification<T>(EventHandler<T> handlers, object sender, T value) => Post(() =>
    {
        foreach (EventHandler<T> handler in handlers.GetInvocationList())
            try { handler(sender, value); }
            catch (Exception ex) { logger.LogWarning(ex, "Measurement subscriber failed"); }
    });

    private void Drain()
    {
        if (_depth != 0 || _draining) return;
        _draining = true;
        try
        {
            while (_pending.Count != 0 || _afterNotifications.Count != 0)
            {
                var notification = _pending.Count != 0 ? _pending.Dequeue() : _afterNotifications.Dequeue();
                try { notification(); }
                catch (Exception ex) { logger.LogWarning(ex, "Measurement subscriber failed"); }
            }
        }
        finally { _draining = false; }
    }

    private sealed class Scope(MeasurementNotificationQueue owner) : IDisposable
    {
        public void Dispose() { owner._depth--; owner.Drain(); }
    }
}
