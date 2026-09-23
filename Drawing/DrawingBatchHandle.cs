using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>A batch owns exactly one visual. Replacing data preserves its identity and order.</summary>
public sealed class DrawingBatchHandle : IDisposable
{
    private readonly DrawingLayer _layer;
    private volatile bool _disposed;
    internal DrawingVisual Visual { get; } = new();
    internal DrawingElement[] Elements { get; private set; }
    internal bool NeedsScale => Elements.Any(e => e.ScaleMode != OverlayScaleMode.None);
    internal DrawingBatchHandle(DrawingLayer layer, DrawingElement[] elements) { _layer = layer; Elements = elements; }
    internal static DrawingElement[] Snapshot(IEnumerable<DrawingElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        Dictionary<Brush, Brush>? brushes = null;
        // Own the array, but immutable elements with frozen brushes can be shared.
        var snapshot = elements.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i] = (snapshot[i] ?? throw new ArgumentException("Null drawing element.", nameof(elements))).Snapshot(ref brushes);
        return snapshot;
    }
    internal void Commit(DrawingElement[] elements, double scale, double dpi)
    {
        var resources = new DrawingResources();
        // RenderOpen records commands; Close publishes them to the visual.
        // Intentionally do NOT use using/finally: disposing after a Draw failure
        // would publish partial content. An unclosed WPF visual drawing context
        // only holds managed recording data and can be discarded on failure.
        // Avoid DrawingGroup.Open, which materializes geometry/drawing objects
        // for every primitive instead of recording compact render commands.
        var context = Visual.RenderOpen();
        foreach (var element in elements) element.Draw(context, scale, dpi, resources);
        context.Close();
        Elements = elements;
    }
    public void Replace(IEnumerable<DrawingElement> elements)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = Snapshot(elements);
        _layer.Owner.Invoke(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _layer.EnsureAlive();
            Commit(snapshot, _layer.Owner.Scale, _layer.PixelsPerDip);
        });
    }
    public void Dispose()
    {
        if (_disposed) return;
        _layer.Owner.InvokeRemoval(() => _layer.Remove(this));
    }
    internal void Invalidate()
    {
        _disposed = true; Elements = [];
        using var context = Visual.RenderOpen();
    }
}

public sealed class BatchClickedEventArgs(DrawingBatchHandle batch, Point imagePosition, MouseButton button) : EventArgs
{
    public DrawingBatchHandle Batch { get; } = batch;
    public Point ImagePosition { get; } = imagePosition;
    public MouseButton Button { get; } = button;
}

