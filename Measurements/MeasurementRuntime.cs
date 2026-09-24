using Fizzy.ImageViewer.Imaging.Queries;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>STA access and shared query/notification execution for measurement handles.</summary>
internal sealed class MeasurementRuntime(ViewerLifetime lifetime, Dispatcher dispatcher,
    PixelQueryScheduler scheduler, ILogger logger)
{
    private bool _stopped;
    internal MeasurementNotificationQueue Notifications { get; } = new(logger);
    internal void RunUiCallback(Action action)
    {
        try { action(); }
        catch (Exception error) { logger.LogWarning(error, "Measurement window callback failed"); }
    }
    internal void VerifyAccess() => dispatcher.VerifyAccess();
    internal void Stop() => _stopped = true;
    internal T Invoke<T>(Func<T> action) => lifetime.Invoke(dispatcher, () =>
    {
        ObjectDisposedException.ThrowIf(_stopped, this);
        return action();
    });
    internal void Invoke(Action action) => Invoke(() => { action(); return true; });
    internal void InvokeRemoval(Action action) => lifetime.InvokeRemoval(dispatcher, () =>
    {
        if (!_stopped) action();
    });
    internal QuerySubscription Register(IFrameQueryClient client) => scheduler.Register(client);
}
