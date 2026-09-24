using System.Windows;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable image-space geometry. Rectangles are always normalized.</summary>
public sealed record MeasurementGeometry
{
    public MeasurementKind Kind { get; }
    private readonly Point _anchor;
    private readonly Point _end;
    private double _radius;
    internal Point Anchor => _anchor;
    /// <summary>First endpoint of a line, or normalized top-left corner of a rectangle.</summary>
    public Point Start => Kind is MeasurementKind.Line or MeasurementKind.Rectangle
        ? _anchor : throw new InvalidOperationException("This geometry has no endpoints.");
    /// <summary>Last endpoint of a line, or normalized bottom-right corner of a rectangle.</summary>
    public Point End => Kind is MeasurementKind.Line or MeasurementKind.Rectangle
        ? _end : throw new InvalidOperationException("This geometry has no endpoints.");
    public Point Position => Kind is MeasurementKind.Point or MeasurementKind.Crosshair
        ? _anchor : throw new InvalidOperationException("This geometry is not a point or crosshair.");
    public Point Center => Kind == MeasurementKind.Circle
        ? _anchor : throw new InvalidOperationException("This geometry is not a circle.");
    public double Radius => Kind == MeasurementKind.Circle
        ? _radius : throw new InvalidOperationException("This geometry is not a circle.");
    public override string ToString() => Kind switch
    {
        MeasurementKind.Point or MeasurementKind.Crosshair => $"{Kind} {{ Position = {Position} }}",
        MeasurementKind.Circle => $"Circle {{ Center = {Center}, Radius = {Radius} }}",
        _ => $"{Kind} {{ Start = {Start}, End = {End} }}"
    };
    /// <summary>Normalized image-space bounds; point and crosshair have zero extent.</summary>
    public Rect Bounds => Kind == MeasurementKind.Circle
        ? new Rect(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2)
        : new Rect(_anchor, _end);

    private MeasurementGeometry(MeasurementKind kind, Point start, Point end)
    {
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) ||
            !double.IsFinite(end.X) || !double.IsFinite(end.Y) ||
            !double.IsFinite(end.X - start.X) || !double.IsFinite(end.Y - start.Y))
            throw new ArgumentOutOfRangeException(nameof(start));
        Kind = kind; _anchor = start; _end = end;
    }

    public static MeasurementGeometry Rectangle(Point a, Point b)
    {
        var (start, end) = NormalizeRectangle(a, b);
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
        return new(MeasurementKind.Circle, center, center) { _radius = radius };
    }

    internal PixelRegion ToRegion(Frames.FrameDescriptor descriptor) => Kind == MeasurementKind.Rectangle
        ? PixelRegion.Clip(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, descriptor) : throw new InvalidOperationException("Not a region.");

    internal IReadOnlyList<Point> ControlPoints => Kind switch
    {
        MeasurementKind.Rectangle => RectangleControlPoints(Start, End),
        MeasurementKind.Line => [Start, End],
        MeasurementKind.Circle => [Center, new(Center.X + Radius, Center.Y)],
        _ => [Position]
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
        MeasurementKind.Circle when index == 1 => Circle(Center, (point - Center).Length),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static (Point Start, Point End) NormalizeRectangle(Point a, Point b)
    {
        EnsureFinite(a); EnsureFinite(b);
        return (new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }
    private static IReadOnlyList<Point> RectangleControlPoints(Point start, Point end) => [start, new(end.X, start.Y), end, new(start.X, end.Y)];
    private static void EnsureFinite(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
    }
}
