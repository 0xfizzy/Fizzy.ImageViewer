using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

public sealed record CircleElement(Point Center, double Radius, Brush Stroke, double Thickness = 1, Brush? Fill = null) : DrawingElement
{
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Center.X, Center.Y); Positive(Radius, nameof(Radius)); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.ScaleWithImage, OverlayScaleMode.FixedStroke, OverlayScaleMode.FixedSize);
        var stroke = BrushSnapshots.Copy(Stroke, ref brushes);
        var fill = Fill == null ? null : BrushSnapshots.Copy(Fill, ref brushes);
        return ReferenceEquals(stroke, Stroke) && ReferenceEquals(fill, Fill)
            ? this : this with { Stroke = stroke, Fill = fill };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources)
    {
        double radius = ScaleMode == OverlayScaleMode.FixedSize ? Radius / scale : Radius;
        context.DrawEllipse(Fill, Pen(Stroke, Thickness, scale, resources), Center, radius, radius);
    }
}
