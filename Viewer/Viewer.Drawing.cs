using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Layers.ViewerLayers Layers => _host.Window.Layers;
    public Drawing.DrawingLayer Markers => Layers.Markers;

    /// <summary>Creates a drawing initially containing one line in the Markers layer. The handle can replace its entire content.</summary>
    public Drawing.DrawingHandle DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0) =>
        Layers.Markers.Add(new Drawing.LineElement(p1, p2, brush, thickness));

    public Drawing.DrawingHandle DrawText(Point anchor, string text, Brush brush, double fontSize = 14, Vector offset = default) =>
        Layers.Markers.Add(new Drawing.TextElement(anchor, text, brush, fontSize, offset));

    public Drawing.DrawingHandle DrawCrosshair(Point center, Brush brush, double armLength = 20, double thickness = 2) =>
        Layers.Markers.Add(new Drawing.CrosshairElement(center, brush, armLength, thickness));

    public Drawing.DrawingHandle DrawRectangle(Rect rect, Brush brush, double thickness = 1.0) =>
        Layers.Markers.Add(new Drawing.RectangleElement(rect, brush, thickness));

    public Drawing.DrawingHandle DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null) =>
        Layers.Markers.Add(new Drawing.CircleElement(center, radius, brush, thickness, fill));

}
