namespace Fizzy.ImageViewer.Imaging.Queries;

/// <summary>All time and thread boundaries are replaceable without constructing a Window.</summary>
internal interface IQueryRuntime
{
    TimeSpan Now { get; }
    IDisposable StartTicks(Action tick);
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token);
    Task PublishAsync(Action action, CancellationToken token);
}
