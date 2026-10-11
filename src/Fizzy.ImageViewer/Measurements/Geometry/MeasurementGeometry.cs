using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Closed immutable geometry family in source-image coordinates.</summary>
/// <remarks>Positions are continuous: (0, 0) is the top-left image boundary and (0.5, 0.5)
/// is the first pixel center. No rounding or pixel-center offset is applied.</remarks>
public abstract record MeasurementGeometry
{
    private protected MeasurementGeometry() { }
    /// <summary>Geometric shape, independent of the tool that created it.</summary>
    public abstract MeasurementGeometryKind Kind { get; }
    /// <summary>Normalized image-space bounds. Points and crosshairs have zero extent; circles use diameter bounds.</summary>
    public abstract Rect Bounds { get; }
    internal abstract Point Anchor { get; }

    /// <summary>Creates a point at a finite image-pixel position.</summary>
    public static PointMeasurementGeometry Point(Point position) => new(position);
    /// <summary>Creates a crosshair at a finite image-pixel position.</summary>
    public static CrosshairMeasurementGeometry Crosshair(Point position) => new(position);
    /// <summary>Creates a line preserving endpoint order. Coordinates and their component differences must be finite.</summary>
    public static LineMeasurementGeometry Line(Point start, Point end) => new(start, end);
    /// <summary>Creates a rectangle from opposite corners, normalizing their order. Coordinates and extents must be finite.</summary>
    public static RectangleMeasurementGeometry Rectangle(Point a, Point b) => new(a, b);
    /// <summary>Creates a circle in image pixels. Center, nonnegative radius and resulting bounds must be finite.</summary>
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
