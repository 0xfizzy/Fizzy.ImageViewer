using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Layers.ViewerLayers Layers => _host.Window.Layers;

    /// <summary>Draws a non-interactive single-element batch in the Markers layer.</summary>
    public IDisposable DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0) =>
        Layers.Markers.AddBatch([new Drawing.LineElement(p1, p2, brush, thickness)]);

    public IDisposable DrawText(Point anchor, string text, Brush brush, int fontSize = 14, Vector offset = default) =>
        Layers.Markers.AddBatch([new Drawing.TextElement(anchor, text, brush, fontSize, offset)]);

    public IDisposable DrawCrosshair(Point center, Brush brush, double size = 20, double thickness = 2) =>
        Layers.Markers.AddBatch([new Drawing.CrosshairElement(center, brush, size, thickness)]);

    public IDisposable DrawRectangle(Rect rect, Brush brush, double thickness = 1.0) =>
        Layers.Markers.AddBatch([new Drawing.RectangleElement(rect, brush, thickness)]);

    public IDisposable DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null) =>
        Layers.Markers.AddBatch([new Drawing.CircleElement(center, radius, brush, thickness, fill)]);

    /// <summary>Clears all business layers, including measurements, without clearing the HUD.</summary>
    public void ClearShapes() => Layers.Clear();
    /// <summary>Creates HUD text with fixed layout.</summary>
    public HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft, double fontSize = 14)
        => _host.Hud.Add(text, brush, anchor, alignment, fontSize);
}
