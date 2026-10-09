using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Text anchored in image coordinates; supports ScaleWithImage and FixedSize, defaulting to FixedSize.</summary>
public sealed record TextElement : DrawingElement
{
    /// <summary>Finite anchor position in image pixels.</summary>
    public Point Anchor { get; init; }
    /// <summary>Non-null text to display.</summary>
    public string Text { get; init; }
    /// <summary>Text brush, snapshotted when submitted.</summary>
    public Brush Foreground { get; init; }
    /// <summary>Finite positive font size; screen DIPs in FixedSize mode, image units in ScaleWithImage mode.</summary>
    public double FontSize { get; init; }
    /// <summary>Finite displacement from the anchor; screen DIPs in FixedSize mode, image units in ScaleWithImage mode.</summary>
    public Vector Offset { get; init; }
    /// <summary>Nonblank WPF font family name; defaults to Segoe UI.</summary>
    public string FontFamily { get; init; } = "Segoe UI";
    /// <summary>Creates text in FixedSize mode with font size and offset in screen DIPs.</summary>
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
