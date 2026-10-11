using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

// Per-preparation cache: never shared across threads or retained across frames.
// Bound the cache for inputs with a distinct style for every element.
internal sealed class DrawingResources
{
    private const int Capacity = 128;
    private Brush? _firstBrush;
    private double _firstThickness;
    private Pen? _firstPen;
    private Dictionary<(Brush Brush, double Thickness), Pen>? _pens;

    internal Pen GetPen(Brush brush, double thickness)
    {
        if (_firstPen != null && ReferenceEquals(_firstBrush, brush) && _firstThickness == thickness)
            return _firstPen;
        var key = (brush, thickness);
        if (_pens != null && _pens.TryGetValue(key, out var cached)) return cached;
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        if (_firstPen == null)
        {
            _firstBrush = brush; _firstThickness = thickness; _firstPen = pen;
            return pen;
        }
        _pens ??= new();
        if (_pens.Count < Capacity - 1) _pens.TryAdd(key, pen);
        return pen;
    }
}
