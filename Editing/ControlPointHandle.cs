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
    public static Brush HandleBrush { get; set; } = Brushes.Orange;
    public const double HandleRadius = 5.0;

    /// <summary>
    /// Create a control point handle at the specified position.
    /// </summary>
    /// <param name="position">Position in image coordinates</param>
    /// <param name="parentShape">The shape this control point belongs to</param>
    /// <param name="pointIndex">Index of this control point</param>
    public static Path CreateHandle(Point position, UIElement parentShape, int pointIndex)
    {
        var geometry = new EllipseGeometry(new Point(0, 0), HandleRadius, HandleRadius);

        // Use FixedSize mode so handle stays constant size during zoom
        var tagData = new OverlayTagData(OverlayScaleMode.FixedSize, ShapeType.Point)
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
