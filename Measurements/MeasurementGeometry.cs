using Fizzy.ImageViewer.Drawing;
using System.Windows;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Geometry;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable image-space geometry. Rectangles are always normalized.</summary>
public sealed record MeasurementGeometry
{
    public ShapeType Kind { get; }
    public Point Start { get; }
    public Point End { get; }
    public double Radius { get; private init; }
    public double X => Start.X;
    public double Y => Start.Y;
    public double Width => End.X - Start.X;
    public double Height => End.Y - Start.Y;

    private MeasurementGeometry(ShapeType kind, Point start, Point end)
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
        return new(ShapeType.Rectangle, start, end);
    }
    public static MeasurementGeometry Line(Point start, Point end) => new(ShapeType.Line, start, end);
    public static MeasurementGeometry Point(Point point) => new(ShapeType.Point, point, point);

    public static MeasurementGeometry Crosshair(Point point) => new(ShapeType.Crosshair, point, point);
    public static MeasurementGeometry Circle(Point center, double radius)
    {
        if (!double.IsFinite(radius) || radius < 0 || !double.IsFinite(center.X + radius) || !double.IsFinite(center.Y + radius) ||
            !double.IsFinite(center.X - radius) || !double.IsFinite(center.Y - radius) || !double.IsFinite(radius * 2))
            throw new ArgumentOutOfRangeException(nameof(radius));
        return new(ShapeType.Circle, center, center) { Radius = radius };
    }

    internal PixelRegion ToRegion(Frames.FrameDescriptor descriptor) => Kind == ShapeType.Rectangle
        ? PixelRegion.Clip(X, Y, Width, Height, descriptor) : throw new InvalidOperationException("Not a region.");

    internal IReadOnlyList<Point> ControlPoints => Kind switch
    {
        ShapeType.Rectangle => GeometryOperations.RectangleControlPoints(Start, End),
        ShapeType.Line => [Start, End],
        ShapeType.Circle => [Start, new(Start.X + Radius, Start.Y)],
        _ => [Start]
    };

    // Use the drag-start snapshot so crossing an opposite corner never changes the fixed anchor.
    internal MeasurementGeometry MoveControlPoint(int index, Point point) => Kind switch
    {
        ShapeType.Rectangle when index is >= 0 and < 4 => Rectangle(ControlPoints[(index + 2) % 4], point),
        ShapeType.Line when index == 0 => Line(point, End),
        ShapeType.Line when index == 1 => Line(Start, point),
        ShapeType.Point when index == 0 => Point(point),
        ShapeType.Crosshair when index == 0 => Crosshair(point),
        ShapeType.Circle when index == 0 => Circle(point, Radius),
        ShapeType.Circle when index == 1 => Circle(Start, (point - Start).Length),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
