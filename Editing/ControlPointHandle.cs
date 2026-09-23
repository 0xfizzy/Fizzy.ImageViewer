using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer.Editing;

/// <summary>
/// Factory for creating control point visual handles.
/// </summary>
internal static class ControlPointHandle
{
    private static Brush HandleBrush => Brushes.Orange;
    public const double HandleRadius = 5.0;

    /// <summary>
    /// Create a control point handle at the specified position.
    /// </summary>
    /// <param name="position">Position in image coordinates</param>
    public static Path CreateHandle(Point position)
    {
        var geometry = new EllipseGeometry(new Point(0, 0), HandleRadius, HandleRadius);

        // Use FixedSize mode so handle stays constant size during zoom
        var tagData = new OverlayShapeData(OverlayScaleMode.FixedSize, ShapeType.Point)
        {
            AnchorPoint = position,
            OriginalBrush = HandleBrush
        };

        var path = new Path
        {
            Fill = HandleBrush,
            Stroke = Brushes.Black,
            StrokeThickness = 1.0,
            Data = geometry,
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = tagData
        };

        return path;
    }
}
