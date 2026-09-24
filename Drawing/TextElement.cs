using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

public sealed record TextElement : DrawingElement
{
    public Point Anchor { get; init; }
    public string Text { get; init; }
    public Brush Foreground { get; init; }
    public double FontSize { get; init; }
    public Vector Offset { get; init; }
    public string FontFamily { get; init; } = "Segoe UI";
    public TextElement(Point anchor, string text, Brush foreground, double fontSize = 14, Vector offset = default)
    { Anchor = anchor; Text = text; Foreground = foreground; FontSize = fontSize; Offset = offset; ScaleMode = OverlayScaleMode.FixedSize; }
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Anchor.X, Anchor.Y, Offset.X, Offset.Y); Positive(FontSize, nameof(FontSize));
        ArgumentNullException.ThrowIfNull(Text); ArgumentException.ThrowIfNullOrWhiteSpace(FontFamily);
        ValidateMode(OverlayScaleMode.ScaleWithImage, OverlayScaleMode.FixedSize);
        var foreground = BrushSnapshots.Copy(Foreground, ref brushes);
        return ReferenceEquals(foreground, Foreground) ? this : this with { Foreground = foreground };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources)
    {
        var text = new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily), FontSize, Foreground, pixelsPerDip);
        double factor = ScaleMode == OverlayScaleMode.FixedSize ? 1 / scale : 1;
        context.PushTransform(new MatrixTransform(factor, 0, 0, factor, Anchor.X + Offset.X * factor, Anchor.Y + Offset.Y * factor));
        context.DrawText(text, new Point());
        context.Pop();
    }
}
