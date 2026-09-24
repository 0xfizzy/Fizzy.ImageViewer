using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

public sealed record RectangleElement(Rect Bounds, Brush Stroke, double Thickness = 1, Brush? Fill = null) : DrawingElement
{
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.ScaleWithImage, OverlayScaleMode.FixedStroke);
        var stroke = BrushSnapshots.Copy(Stroke, ref brushes);
        var fill = Fill == null ? null : BrushSnapshots.Copy(Fill, ref brushes);
        return ReferenceEquals(stroke, Stroke) && ReferenceEquals(fill, Fill)
            ? this : this with { Stroke = stroke, Fill = fill };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources) =>
        context.DrawRectangle(Fill, Pen(Stroke, Thickness, scale, resources), Bounds);
}
