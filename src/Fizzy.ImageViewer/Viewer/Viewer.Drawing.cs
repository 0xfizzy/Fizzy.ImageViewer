using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc/>
    public Layers.ViewerLayers Layers => _host.Control.Layers;
    /// <inheritdoc/>
    public Drawing.DrawingLayer Markers => Layers.Markers;

    /// <summary>Creates a drawing initially containing one line in the Markers layer. The handle can replace its entire content.</summary>
    public Drawing.DrawingHandle DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0) =>
        _host.Control.DrawLine(p1, p2, brush, thickness);

    /// <inheritdoc/>
    public Drawing.DrawingHandle DrawText(Point anchor, string text, Brush brush, double fontSize = 14, Vector offset = default) =>
        _host.Control.DrawText(anchor, text, brush, fontSize, offset);

    /// <inheritdoc/>
    public Drawing.DrawingHandle DrawCrosshair(Point center, Brush brush, double armLength = 20, double thickness = 2) =>
        _host.Control.DrawCrosshair(center, brush, armLength, thickness);

    /// <inheritdoc/>
    public Drawing.DrawingHandle DrawRectangle(Rect rect, Brush brush, double thickness = 1.0) =>
        _host.Control.DrawRectangle(rect, brush, thickness);

    /// <inheritdoc/>
    public Drawing.DrawingHandle DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null) =>
        _host.Control.DrawCircle(center, radius, brush, thickness, fill);

}
