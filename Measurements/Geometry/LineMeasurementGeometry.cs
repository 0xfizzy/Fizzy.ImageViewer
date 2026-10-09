using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

public sealed record LineMeasurementGeometry : MeasurementGeometry
{
    public Point Start { get; }
    public Point End { get; }
    /// <summary>Euclidean length in image pixels; positive infinity when the length exceeds Double.MaxValue.</summary>
    public double Length => Distance(Start, End);
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Line;
    public override Rect Bounds => new(Start, End);
    internal override Point Anchor => Start;
    internal LineMeasurementGeometry(Point start, Point end)
    { EnsureExtent(start, end); Start = start; End = end; }
}
