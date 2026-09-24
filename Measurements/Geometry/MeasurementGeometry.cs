using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Closed immutable geometry family in source-image coordinates.</summary>
public abstract record MeasurementGeometry
{
    private protected MeasurementGeometry() { }
    public abstract MeasurementKind Kind { get; }
    public abstract Rect Bounds { get; }
    internal abstract Point Anchor { get; }
    internal abstract IReadOnlyList<Point> ControlPoints { get; }
    internal abstract MeasurementGeometry MoveControlPoint(int index, Point point);

    public static PointMeasurementGeometry Point(Point position) => new(position);
    public static CrosshairMeasurementGeometry Crosshair(Point position) => new(position);
    public static LineMeasurementGeometry Line(Point start, Point end) => new(start, end);
    public static RectangleMeasurementGeometry Rectangle(Point a, Point b) => new(a, b);
    public static CircleMeasurementGeometry Circle(Point center, double radius) => new(center, radius);

    internal static void EnsureFinite(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
    }
    internal static void EnsureExtent(Point start, Point end)
    {
        EnsureFinite(start); EnsureFinite(end);
        if (!double.IsFinite(end.X - start.X) || !double.IsFinite(end.Y - start.Y))
            throw new ArgumentOutOfRangeException(nameof(end));
    }
}
