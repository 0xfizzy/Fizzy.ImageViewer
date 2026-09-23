using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

internal sealed class BatchVisualHost : FrameworkElement
{
    private readonly VisualCollection _visuals;
    internal BatchVisualHost() => _visuals = new VisualCollection(this);
    internal Action? DpiChanged { get; set; }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        DpiChanged?.Invoke();
    }
    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => null;
    internal void Add(DrawingVisual visual) => _visuals.Add(visual);
    internal void Remove(DrawingVisual visual) => _visuals.Remove(visual);
    internal void Clear() => _visuals.Clear();
    internal int Count => _visuals.Count;
}
