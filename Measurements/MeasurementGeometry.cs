using System.Windows;
using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Geometry;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable, versioned image-space geometry. Rectangles are always normalized.</summary>
internal sealed record MeasurementGeometry
{
    public ShapeType Kind { get; }
    public Point Start { get; }
    public Point End { get; }
    public long Version { get; }
    public double X => Start.X;
    public double Y => Start.Y;
    public double Width => End.X - Start.X;
    public double Height => End.Y - Start.Y;

    private MeasurementGeometry(ShapeType kind, Point start, Point end, long version)
    {
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) ||
            !double.IsFinite(end.X) || !double.IsFinite(end.Y) ||
            !double.IsFinite(end.X - start.X) || !double.IsFinite(end.Y - start.Y))
            throw new ArgumentOutOfRangeException(nameof(start));
        Kind = kind; Start = start; End = end; Version = version;
    }

    public static MeasurementGeometry Rectangle(Point a, Point b, long version = 0)
    {
        var (start, end) = GeometryOperations.NormalizeRectangle(a, b);
        return new(ShapeType.Rectangle, start, end, version);
    }
    public static MeasurementGeometry Line(Point start, Point end, long version = 0) => new(ShapeType.Line, start, end, version);
    public static MeasurementGeometry Point(Point point, long version = 0) => new(ShapeType.Point, point, point, version);

    public MeasurementGeometry WithVersion(long version) => new(Kind, Start, End, version);
    public PixelRegion ToRegion(Frames.FrameDescriptor descriptor) => Kind == ShapeType.Rectangle
        ? PixelRegion.Clip(X, Y, Width, Height, descriptor) : throw new InvalidOperationException("Not a region.");

    public IReadOnlyList<Point> ControlPoints => Kind switch
    {
        ShapeType.Rectangle => GeometryOperations.RectangleControlPoints(Start, End),
        ShapeType.Line => [Start, End],
        _ => [Start]
    };

    // Use the drag-start snapshot so crossing an opposite corner never changes the fixed anchor.
    public MeasurementGeometry MoveControlPoint(int index, Point point) => Kind switch
    {
        ShapeType.Rectangle when index is >= 0 and < 4 => Rectangle(ControlPoints[(index + 2) % 4], point),
        ShapeType.Line when index == 0 => Line(point, End),
        ShapeType.Line when index == 1 => Line(Start, point),
        ShapeType.Point when index == 0 => Point(point),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
