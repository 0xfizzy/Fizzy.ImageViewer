using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Closed immutable geometry family in source-image coordinates.</summary>
public abstract record MeasurementGeometry
{
    private protected MeasurementGeometry() { }
    public abstract MeasurementGeometryKind Kind { get; }
    public abstract Rect Bounds { get; }
    internal abstract Point Anchor { get; }

    public static PointMeasurementGeometry Point(Point position) => new(position);
    public static CrosshairMeasurementGeometry Crosshair(Point position) => new(position);
    public static LineMeasurementGeometry Line(Point start, Point end) => new(start, end);
    public static RectangleMeasurementGeometry Rectangle(Point a, Point b) => new(a, b);
    public static CircleMeasurementGeometry Circle(Point center, double radius) => new(center, radius);

    internal static double Distance(Point a, Point b)
    {
        double x = Math.Abs(b.X - a.X), y = Math.Abs(b.Y - a.Y);
        double scale = Math.Max(x, y);
        if (scale == 0 || double.IsPositiveInfinity(scale)) return scale;
        double ratio = Math.Min(x, y) / scale;
        return scale * Math.Sqrt(1 + ratio * ratio);
    }

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
