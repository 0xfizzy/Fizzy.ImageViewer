using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Normalized rectangle in source-image pixels.</summary>
public sealed record RectangleMeasurementGeometry : MeasurementGeometry
{
    /// <summary>Normalized minimum X and Y corner in source-image pixels.</summary>
    public Point TopLeft { get; }
    /// <summary>Normalized maximum X and Y corner in source-image pixels.</summary>
    public Point BottomRight { get; }
    /// <inheritdoc/>
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Rectangle;
    /// <inheritdoc/>
    public override Rect Bounds => new(TopLeft, BottomRight);
    internal override Point Anchor => TopLeft;
    internal RectangleMeasurementGeometry(Point a, Point b)
    {
        EnsureExtent(a, b);
        TopLeft = new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        BottomRight = new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
}
