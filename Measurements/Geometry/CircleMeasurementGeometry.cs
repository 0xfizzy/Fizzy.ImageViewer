using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

public sealed record CircleMeasurementGeometry : MeasurementGeometry
{
    public Point Center { get; }
    public double Radius { get; }
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Circle;
    public override Rect Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
    internal override Point Anchor => Center;
    internal CircleMeasurementGeometry(Point center, double radius)
    {
        EnsureFinite(center);
        if (!double.IsFinite(radius) || radius < 0 || !double.IsFinite(center.X + radius) ||
            !double.IsFinite(center.Y + radius) || !double.IsFinite(center.X - radius) ||
            !double.IsFinite(center.Y - radius) || !double.IsFinite(radius * 2))
            throw new ArgumentOutOfRangeException(nameof(radius));
        Center = center; Radius = radius;
    }
}
