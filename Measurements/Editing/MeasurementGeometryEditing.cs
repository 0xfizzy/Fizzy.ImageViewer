using System.Windows;

namespace Fizzy.ImageViewer.Measurements.Editing;

/// <summary>Control ordering and drag behavior for the closed geometry family.</summary>
internal static class MeasurementGeometryEditing
{
    internal static IReadOnlyList<Point> GetControlPoints(MeasurementGeometry geometry) => geometry switch
    {
        PointMeasurementGeometry point => [point.Position],
        CrosshairMeasurementGeometry crosshair => [crosshair.Position],
        LineMeasurementGeometry line => [line.Start, line.End],
        RectangleMeasurementGeometry rectangle => [rectangle.TopLeft,
            new(rectangle.BottomRight.X, rectangle.TopLeft.Y), rectangle.BottomRight,
            new(rectangle.TopLeft.X, rectangle.BottomRight.Y)],
        CircleMeasurementGeometry circle => [circle.Center, new(circle.Center.X + circle.Radius, circle.Center.Y)],
        _ => throw new ArgumentOutOfRangeException(nameof(geometry))
    };

    internal static MeasurementGeometry MoveControlPoint(MeasurementGeometry geometry, int index, Point point) => (geometry, index) switch
    {
        (PointMeasurementGeometry, 0) => MeasurementGeometry.Point(point),
        (CrosshairMeasurementGeometry, 0) => MeasurementGeometry.Crosshair(point),
        (LineMeasurementGeometry line, 0) => MeasurementGeometry.Line(point, line.End),
        (LineMeasurementGeometry line, 1) => MeasurementGeometry.Line(line.Start, point),
        // Keep the opposite corner from the drag-start snapshot, even after crossing it.
        (RectangleMeasurementGeometry rectangle, >= 0 and < 4) =>
            MeasurementGeometry.Rectangle(GetControlPoints(rectangle)[(index + 2) % 4], point),
        (CircleMeasurementGeometry circle, 0) => MeasurementGeometry.Circle(point, circle.Radius),
        (CircleMeasurementGeometry circle, 1) => MeasurementGeometry.Circle(circle.Center, MeasurementGeometry.Distance(circle.Center, point)),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
