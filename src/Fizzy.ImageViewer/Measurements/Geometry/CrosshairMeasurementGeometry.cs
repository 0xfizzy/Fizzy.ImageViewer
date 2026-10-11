using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Crosshair location in source-image pixels; marker size is not part of its geometry.</summary>
public sealed record CrosshairMeasurementGeometry : MeasurementGeometry
{
    /// <summary>Finite crosshair center in source-image pixels.</summary>
    public Point Position { get; }
    /// <inheritdoc/>
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Crosshair;
    /// <inheritdoc/>
    public override Rect Bounds => new(Position, Position);
    internal override Point Anchor => Position;
    internal CrosshairMeasurementGeometry(Point position) { EnsureFinite(position); Position = position; }
}
