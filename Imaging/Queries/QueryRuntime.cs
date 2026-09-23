using System.Diagnostics;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Imaging.Queries;

/// <summary>All time and thread boundaries are replaceable without constructing a Window.</summary>
internal interface IQueryRuntime
{
    TimeSpan Now { get; }
    IDisposable StartTicks(Action tick);
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token);
    Task PublishAsync(Action action, CancellationToken token);
}

internal sealed class DispatcherQueryRuntime(Dispatcher dispatcher) : IQueryRuntime
{
    private readonly long _started = Stopwatch.GetTimestamp();
    public TimeSpan Now => Stopwatch.GetElapsedTime(_started);
    public IDisposable StartTicks(Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(10) };
        EventHandler handler = (_, _) => tick();
        timer.Tick += handler; timer.Start();
        return new QuerySubscription(() => { timer.Stop(); timer.Tick -= handler; });
    }
    public Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token) => Task.Run(action, token);
    public Task PublishAsync(Action action, CancellationToken token) => dispatcher.InvokeAsync(action, DispatcherPriority.Background, token).Task;
}

