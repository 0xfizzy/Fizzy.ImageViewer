using System.Windows.Threading;

namespace Fizzy.ImageViewer;

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
    // Owners release remaining content during shutdown; retained handles become no-ops.
    internal void InvokeRemoval(Dispatcher dispatcher, Action action)
    {
        if (IsStopping) return;
        bool started = false;
        try
        {
            dispatcher.Invoke(() =>
            {
                if (IsStopping) return;
                started = true;
                action();
            });
        }
        catch (OperationCanceledException) when (!started && IsStopping) { }
        catch (InvalidOperationException) when (!started && IsStopping) { }
    }

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
