using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Point location in source-image pixels; marker size is not part of its geometry.</summary>
public sealed record PointMeasurementGeometry : MeasurementGeometry
{
    /// <summary>Finite point location in source-image pixels.</summary>
    public Point Position { get; }
    /// <inheritdoc/>
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Point;
    /// <inheritdoc/>
    public override Rect Bounds => new(Position, Position);
    internal override Point Anchor => Position;
    internal PointMeasurementGeometry(Point position) { EnsureFinite(position); Position = position; }
}
