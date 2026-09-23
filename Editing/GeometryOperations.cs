using System.Windows;

namespace Fizzy.ImageViewer.Editing;

internal static class GeometryOperations
{
    internal static (Point Start, Point End) NormalizeRectangle(Point a, Point b)
    {
        EnsureFinite(a); EnsureFinite(b);
        return (new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }
    internal static IReadOnlyList<Point> RectangleControlPoints(Point start, Point end) => [start, new(end.X, start.Y), end, new(start.X, end.Y)];
    internal static void EnsureFinite(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
    }
}
