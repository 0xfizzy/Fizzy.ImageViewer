using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

public sealed record LineMeasurementGeometry : MeasurementGeometry
{
    public Point Start { get; }
    public Point End { get; }
    public double Length => (End - Start).Length;
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Line;
    public override Rect Bounds => new(Start, End);
    internal override Point Anchor => Start;
    internal override IReadOnlyList<Point> ControlPoints => [Start, End];
    internal LineMeasurementGeometry(Point start, Point end)
    { EnsureExtent(start, end); Start = start; End = end; }
    internal override MeasurementGeometry MoveControlPoint(int index, Point point) => index switch
    {
        0 => Line(point, End), 1 => Line(Start, point),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
