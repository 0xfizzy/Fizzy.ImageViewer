using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

public sealed record LineElement(Point Start, Point End, Brush Stroke, double Thickness = 1) : DrawingElement
{
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Start.X, Start.Y, End.X, End.Y); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.ScaleWithImage, OverlayScaleMode.FixedStroke);
        var stroke = BrushSnapshots.Copy(Stroke, ref brushes);
        return ReferenceEquals(stroke, Stroke) ? this : this with { Stroke = stroke };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources) =>
        context.DrawLine(Pen(Stroke, Thickness, scale, resources), Start, End);
}
