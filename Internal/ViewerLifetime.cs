using System.Windows.Threading;

namespace Fizzy.ImageViewer.Internal;

internal sealed class ViewerLifetime
{
    private int _state; // 0: active, 1: disposing, 2: disposed
    internal object Gate { get; } = new();
    internal bool IsStopping => Volatile.Read(ref _state) != 0;
    internal bool BeginDisposal()
    {
        lock (Gate)
        {
            if (_state != 0) return false;
            Volatile.Write(ref _state, 1);
            return true;
        }
    }
    internal void Complete() => Volatile.Write(ref _state, 2);
    internal void ThrowIfStopping() => ObjectDisposedException.ThrowIf(IsStopping, typeof(Viewer));
    internal T Invoke<T>(Dispatcher dispatcher, Func<T> action)
    {
        ThrowIfStopping();
        bool started = false;
        try
        {
            return dispatcher.Invoke(() =>
            {
                started = true;
                ThrowIfStopping();
                return action();
            });
        }
        catch (OperationCanceledException) when (!started && IsStopping)
        { throw new ObjectDisposedException(nameof(Viewer)); }
        catch (InvalidOperationException) when (!started && IsStopping)
        { throw new ObjectDisposedException(nameof(Viewer)); }
    }
}
