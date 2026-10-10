using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Management and convenience drawing in the default marker layer.</summary>
public interface IViewerDrawing
{
    /// <summary>The entire default drawing layer, including its content and input policy.</summary>
    DrawingLayer Markers { get; }

    /// <summary>
    /// Creates a drawing containing a line on Markers, which excludes hit testing by default. The returned handle can replace its content with one element or a collection.
    /// </summary>
    DrawingHandle DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0);

    /// <summary>
    /// Creates a drawing containing a text label on Markers, which excludes hit testing by default. The returned handle can replace its content with one element or a collection.
    /// </summary>
    DrawingHandle DrawText(Point anchor, string text, Brush brush, double fontSize = 14, Vector offset = default);

    /// <summary>
    /// Creates a drawing containing a crosshair on Markers, which excludes hit testing by default. The returned handle can replace its content with one element or a collection.
    /// </summary>
    DrawingHandle DrawCrosshair(Point center, Brush brush, double armLength = 20, double thickness = 2);

    /// <summary>
    /// Creates a drawing containing a rectangle on Markers, which excludes hit testing by default. The returned handle can replace its content with one element or a collection.
    /// </summary>
    DrawingHandle DrawRectangle(Rect rect, Brush brush, double thickness = 1.0);

    /// <summary>
    /// Creates a drawing containing a circle on Markers, which excludes hit testing by default. The returned handle can replace its content with one element or a collection.
    /// </summary>
    DrawingHandle DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null);
}
