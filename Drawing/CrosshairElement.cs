using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Crosshair with a center ring, anchored in image coordinates. Defaults to a fixed screen size.</summary>
public sealed record CrosshairElement : DrawingElement
{
    /// <summary>Finite center in image pixels.</summary>
    public Point Center { get; init; }
    /// <summary>Outline brush, snapshotted when submitted.</summary>
    public Brush Stroke { get; init; }
    /// <summary>Finite positive distance from the center to each endpoint; screen DIPs in FixedSize mode, otherwise image pixels.</summary>
    public double ArmLength { get; init; }
    /// <summary>Finite positive stroke width; screen DIPs unless ScaleMode is ScaleWithImage.</summary>
    public double Thickness { get; init; }
    /// <summary>Creates a crosshair using FixedSize mode; arm length and stroke width are in screen DIPs.</summary>
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
