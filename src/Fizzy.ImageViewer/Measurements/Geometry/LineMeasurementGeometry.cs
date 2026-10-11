using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Ordered line endpoints in source-image pixels.</summary>
public sealed record LineMeasurementGeometry : MeasurementGeometry
{
    /// <summary>First endpoint in source-image pixels; input order is preserved.</summary>
    public Point Start { get; }
    /// <summary>Second endpoint in source-image pixels; input order is preserved.</summary>
    public Point End { get; }
    /// <summary>Euclidean length in image pixels; positive infinity when the length exceeds Double.MaxValue.</summary>
    public double Length => Distance(Start, End);
    /// <inheritdoc/>
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Line;
    /// <inheritdoc/>
    public override Rect Bounds => new(Start, End);
    internal override Point Anchor => Start;
    internal LineMeasurementGeometry(Point start, Point end)
    { EnsureExtent(start, end); Start = start; End = end; }
}
