using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

public sealed record PointMeasurementGeometry : MeasurementGeometry
{
    public Point Position { get; }
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Point;
    public override Rect Bounds => new(Position, Position);
    internal override Point Anchor => Position;
    internal PointMeasurementGeometry(Point position) { EnsureFinite(position); Position = position; }
}
