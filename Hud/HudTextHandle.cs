using Fizzy.ImageViewer.Drawing;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Hud;

public sealed class HudTextHandle : IDisposable
{
    private Action? _dispose;
    private Action<string, Brush>? _update;
    private int _disposed;
    internal HudTextHandle(Action<string, Brush> update, Action dispose) { _update = update; _dispose = dispose; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public void Update(string text, Brush brush)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(brush);
        var update = Volatile.Read(ref _update) ?? throw new ObjectDisposedException(nameof(HudTextHandle));
        update(text, SnapshotBrush(brush));
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Exchange(ref _update, null);
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
    internal void Invalidate()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Interlocked.Exchange(ref _update, null);
        Interlocked.Exchange(ref _dispose, null);
    }
    internal static Brush SnapshotBrush(Brush brush) => BrushSnapshots.Freeze(brush);
}
