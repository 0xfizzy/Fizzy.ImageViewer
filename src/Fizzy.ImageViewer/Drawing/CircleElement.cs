using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Circle drawing anchored in image coordinates. Radius and stroke units follow ScaleMode.</summary>
/// <param name="Center">Finite center in image pixels.</param>
/// <param name="Radius">Finite positive radius.</param>
/// <param name="Stroke">Outline brush, snapshotted when submitted.</param>
/// <param name="Thickness">Finite positive outline width.</param>
/// <param name="Fill">Optional interior brush; null leaves the interior unfilled.</param>
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
