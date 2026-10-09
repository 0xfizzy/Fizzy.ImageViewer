using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

public sealed record RectangleMeasurementGeometry : MeasurementGeometry
{
    public Point TopLeft { get; }
    public Point BottomRight { get; }
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Rectangle;
    public override Rect Bounds => new(TopLeft, BottomRight);
    internal override Point Anchor => TopLeft;
    internal RectangleMeasurementGeometry(Point a, Point b)
    {
        EnsureExtent(a, b);
        TopLeft = new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        BottomRight = new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
}
