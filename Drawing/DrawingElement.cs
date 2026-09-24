using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Immutable image-coordinate drawing data. Brushes are snapshotted when submitted.</summary>
public abstract record DrawingElement
{
    private protected DrawingElement() { }
    public OverlayScaleMode ScaleMode { get; init; } = OverlayScaleMode.FixedStroke;
    internal abstract DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes);
    internal abstract void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources);
    internal static void Finite(double a, double b, double c = 0, double d = 0)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c) || !double.IsFinite(d)) throw new ArgumentException("Coordinates and dimensions must be finite.");
    }
    internal static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
    internal void ValidateMode(OverlayScaleMode a, OverlayScaleMode b, OverlayScaleMode? c = null)
    {
        if (ScaleMode != a && ScaleMode != b && ScaleMode != c) throw new ArgumentException("Unsupported scale mode for this element.");
    }
    internal Pen Pen(Brush brush, double thickness, double scale, DrawingResources resources) =>
        resources.GetPen(brush, ScaleMode == OverlayScaleMode.ScaleWithImage ? thickness : thickness / scale);
}
