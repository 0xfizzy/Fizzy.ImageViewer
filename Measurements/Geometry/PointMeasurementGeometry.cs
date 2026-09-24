using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

public sealed record PointMeasurementGeometry : MeasurementGeometry
{
    public Point Position { get; }
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Point;
    public override Rect Bounds => new(Position, Position);
    internal override Point Anchor => Position;
    internal override IReadOnlyList<Point> ControlPoints => [Position];
    internal PointMeasurementGeometry(Point position) { EnsureFinite(position); Position = position; }
    internal override MeasurementGeometry MoveControlPoint(int index, Point point) => index == 0
        ? Point(point) : throw new ArgumentOutOfRangeException(nameof(index));
}
