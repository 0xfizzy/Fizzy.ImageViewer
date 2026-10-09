using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

public sealed record CrosshairMeasurementGeometry : MeasurementGeometry
{
    public Point Position { get; }
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Crosshair;
    public override Rect Bounds => new(Position, Position);
    internal override Point Anchor => Position;
    internal CrosshairMeasurementGeometry(Point position) { EnsureFinite(position); Position = position; }
}
