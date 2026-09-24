using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Layers.ViewerLayers Layers => _host.Window.Layers;

    /// <summary>Creates a drawing initially containing one line in the Markers layer. The handle can replace its entire content.</summary>
    public DrawingHandle DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0) =>
        Layers.Markers.Add(new Drawing.LineElement(p1, p2, brush, thickness));

    public DrawingHandle DrawText(Point anchor, string text, Brush brush, double fontSize = 14, Vector offset = default) =>
        Layers.Markers.Add(new Drawing.TextElement(anchor, text, brush, fontSize, offset));

    public DrawingHandle DrawCrosshair(Point center, Brush brush, double size = 20, double thickness = 2) =>
        Layers.Markers.Add(new Drawing.CrosshairElement(center, brush, size, thickness));

    public DrawingHandle DrawRectangle(Rect rect, Brush brush, double thickness = 1.0) =>
        Layers.Markers.Add(new Drawing.RectangleElement(rect, brush, thickness));

    public DrawingHandle DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null) =>
        Layers.Markers.Add(new Drawing.CircleElement(center, radius, brush, thickness, fill));

    /// <summary>Creates HUD text with fixed layout.</summary>
    public HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft, double fontSize = 14)
        => _host.Hud.Add(text, brush, anchor, alignment, fontSize);
}
