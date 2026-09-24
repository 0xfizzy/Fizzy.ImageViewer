using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

public sealed record CrosshairMeasurementGeometry : MeasurementGeometry
{
    public Point Position { get; }
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Crosshair;
    public override Rect Bounds => new(Position, Position);
    internal override Point Anchor => Position;
    internal override IReadOnlyList<Point> ControlPoints => [Position];
    internal CrosshairMeasurementGeometry(Point position) { EnsureFinite(position); Position = position; }
    internal override MeasurementGeometry MoveControlPoint(int index, Point point) => index == 0
        ? Crosshair(point) : throw new ArgumentOutOfRangeException(nameof(index));
}
