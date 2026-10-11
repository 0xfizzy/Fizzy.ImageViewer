using Fizzy.ImageViewer.Drawing;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Hud;

/// <summary>Handle for one viewer-owned screen-space text item. Updates and removal dispatch to the viewer STA.</summary>
public sealed class HudTextHandle : IDisposable
{
    private Action? _dispose;
    private Action<string, Brush>? _update;
    private int _disposed;
    internal HudTextHandle(Action<string, Brush> update, Action dispose) { _update = update; _dispose = dispose; }
    /// <summary>Whether the handle has been disposed or invalidated by viewer shutdown.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    /// <summary>Replaces text and snapshots the brush while preserving layout. Rejects null arguments and a disposed handle.</summary>
    public void Update(string text, Brush brush)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(brush);
        var update = Volatile.Read(ref _update) ?? throw new ObjectDisposedException(nameof(HudTextHandle));
        update(text, SnapshotBrush(brush));
    }
    /// <summary>Removes this text once; safe from any thread and after viewer closure.</summary>
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
