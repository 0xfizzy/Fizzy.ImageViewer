using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

public sealed record CrosshairElement : DrawingElement
{
    public Point Center { get; init; }
    public Brush Stroke { get; init; }
    /// <summary>Distance from the center to each crosshair endpoint.</summary>
    public double ArmLength { get; init; }
    public double Thickness { get; init; }
    public CrosshairElement(Point center, Brush stroke, double armLength = 20, double thickness = 2)
    { Center = center; Stroke = stroke; ArmLength = armLength; Thickness = thickness; ScaleMode = OverlayScaleMode.FixedSize; }
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Center.X, Center.Y); Positive(ArmLength, nameof(ArmLength)); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.ScaleWithImage, OverlayScaleMode.FixedStroke, OverlayScaleMode.FixedSize);
        var stroke = BrushSnapshots.Copy(Stroke, ref brushes);
        return ReferenceEquals(stroke, Stroke) ? this : this with { Stroke = stroke };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources)
    {
        double armLength = ScaleMode == OverlayScaleMode.FixedSize ? ArmLength / scale : ArmLength;
        var pen = Pen(Stroke, Thickness, scale, resources);
        context.DrawLine(pen, new(Center.X - armLength, Center.Y), new(Center.X + armLength, Center.Y));
        context.DrawLine(pen, new(Center.X, Center.Y - armLength), new(Center.X, Center.Y + armLength));
        context.DrawEllipse(null, pen, Center, armLength / 2, armLength / 2);
    }
}
