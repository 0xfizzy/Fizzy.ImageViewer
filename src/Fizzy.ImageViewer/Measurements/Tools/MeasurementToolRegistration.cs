namespace Fizzy.ImageViewer.Measurements;

/// <summary>Releases one registration through its owner's shutdown-safe STA boundary.</summary>
internal sealed class MeasurementToolRegistration(Action revoke) : IDisposable
{
    private Action? _revoke = revoke;
    internal void Invalidate() => Interlocked.Exchange(ref _revoke, null);
    public void Dispose() => Interlocked.Exchange(ref _revoke, null)?.Invoke();
}
