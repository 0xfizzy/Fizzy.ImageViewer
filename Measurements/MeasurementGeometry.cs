using System.Windows;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable image-space geometry. Rectangles are always normalized.</summary>
public sealed record MeasurementGeometry
{
    public MeasurementKind Kind { get; }
    public Point Start { get; }
    public Point End { get; }
    public double Radius { get; private init; }
    public double X => Start.X;
    public double Y => Start.Y;
    /// <summary>Normalized image-space bounds; point and crosshair have zero extent.</summary>
    public Rect Bounds => Kind == MeasurementKind.Circle
        ? new Rect(Start.X - Radius, Start.Y - Radius, Radius * 2, Radius * 2)
        : new Rect(Start, End);

    private MeasurementGeometry(MeasurementKind kind, Point start, Point end)
    {
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) ||
            !double.IsFinite(end.X) || !double.IsFinite(end.Y) ||
            !double.IsFinite(end.X - start.X) || !double.IsFinite(end.Y - start.Y))
            throw new ArgumentOutOfRangeException(nameof(start));
        Kind = kind; Start = start; End = end;
    }

    public static MeasurementGeometry Rectangle(Point a, Point b)
    {
        var (start, end) = GeometryOperations.NormalizeRectangle(a, b);
        return new(MeasurementKind.Rectangle, start, end);
    }
    public static MeasurementGeometry Line(Point start, Point end) => new(MeasurementKind.Line, start, end);
    public static MeasurementGeometry Point(Point point) => new(MeasurementKind.Point, point, point);

    public static MeasurementGeometry Crosshair(Point point) => new(MeasurementKind.Crosshair, point, point);
    public static MeasurementGeometry Circle(Point center, double radius)
    {
        if (!double.IsFinite(radius) || radius < 0 || !double.IsFinite(center.X + radius) || !double.IsFinite(center.Y + radius) ||
            !double.IsFinite(center.X - radius) || !double.IsFinite(center.Y - radius) || !double.IsFinite(radius * 2))
            throw new ArgumentOutOfRangeException(nameof(radius));
        return new(MeasurementKind.Circle, center, center) { Radius = radius };
    }

    internal PixelRegion ToRegion(Frames.FrameDescriptor descriptor) => Kind == MeasurementKind.Rectangle
        ? PixelRegion.Clip(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, descriptor) : throw new InvalidOperationException("Not a region.");

    internal IReadOnlyList<Point> ControlPoints => Kind switch
    {
        MeasurementKind.Rectangle => GeometryOperations.RectangleControlPoints(Start, End),
        MeasurementKind.Line => [Start, End],
        MeasurementKind.Circle => [Start, new(Start.X + Radius, Start.Y)],
        _ => [Start]
    };

    // Use the drag-start snapshot so crossing an opposite corner never changes the fixed anchor.
    internal MeasurementGeometry MoveControlPoint(int index, Point point) => Kind switch
    {
        MeasurementKind.Rectangle when index is >= 0 and < 4 => Rectangle(ControlPoints[(index + 2) % 4], point),
        MeasurementKind.Line when index == 0 => Line(point, End),
        MeasurementKind.Line when index == 1 => Line(Start, point),
        MeasurementKind.Point when index == 0 => Point(point),
        MeasurementKind.Crosshair when index == 0 => Crosshair(point),
        MeasurementKind.Circle when index == 0 => Circle(point, Radius),
        MeasurementKind.Circle when index == 1 => Circle(Start, (point - Start).Length),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
