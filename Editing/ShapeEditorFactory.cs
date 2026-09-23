using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Enums;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

/// <summary>Closed bindings for the supported shapes; each edit owns a fresh session.</summary>
internal static class ShapeEditorFactory
{
    internal static ShapeEditSession? Create(UIElement shape, MeasurementItem? item)
    {
        if (OverlayShapeData.Get(shape) is not { } data) return null;
        IShapeEditor? editor = data.ShapeType switch
        {
            ShapeType.Point or ShapeType.Crosshair => new AnchorEditor(),
            ShapeType.Line => new LineEditor(),
            ShapeType.Rectangle => new RectangleEditor(),
            ShapeType.Circle => new CircleEditor(),
            _ => null
        };
        if (editor == null) return null;
        var session = new ShapeEditSession(shape, item, editor);
        var points = session.Points;
        if (points.Count > 0 && points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y))) return session;
        session.Dispose();
        return null;
    }
}
