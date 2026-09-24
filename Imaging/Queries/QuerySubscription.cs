namespace Fizzy.ImageViewer.Imaging.Queries;

internal sealed class QuerySubscription(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
