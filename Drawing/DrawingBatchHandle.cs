using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Fizzy.ImageViewer.Enums;

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
        var brushes = new Dictionary<Brush, Brush>(ReferenceEqualityComparer.Instance);
        return elements.Select(e => (e ?? throw new ArgumentException("Null drawing element.", nameof(elements))).Snapshot(brushes)).ToArray();
    }
    internal static DrawingGroup Prepare(DrawingElement[] elements, double scale, double dpi)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
            foreach (var element in elements) element.Draw(context, scale, dpi);
        drawing.Freeze();
        return drawing;
    }
    internal void Commit(DrawingElement[] elements, DrawingGroup drawing)
    {
        using (var context = Visual.RenderOpen()) context.DrawDrawing(drawing);
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
            var drawing = Prepare(snapshot, _layer.Owner.Scale, _layer.PixelsPerDip);
            Commit(snapshot, drawing);
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

internal sealed class BatchVisualHost : FrameworkElement
{
    private readonly VisualCollection _visuals;
    internal BatchVisualHost() { _visuals = new VisualCollection(this); }
    internal Action? DpiChanged { get; set; }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        DpiChanged?.Invoke();
    }
    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
    // No rectangular hit surface: only actual drawing content should receive input.
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => null;
    internal void Add(DrawingVisual visual) => _visuals.Add(visual);
    internal void Remove(DrawingVisual visual) => _visuals.Remove(visual);
    internal void Clear() => _visuals.Clear();
    internal int Count => _visuals.Count;
}
