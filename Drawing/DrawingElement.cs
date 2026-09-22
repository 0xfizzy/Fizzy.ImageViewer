using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Immutable image-coordinate drawing data. Brushes are snapshotted when submitted.</summary>
public abstract record DrawingElement
{
    private protected DrawingElement() { }
    public OverlayScaleMode ScaleMode { get; init; } = OverlayScaleMode.FixedStroke;
    internal abstract DrawingElement Snapshot(Dictionary<Brush, Brush> brushes);
    internal abstract void Draw(DrawingContext context, double scale, double pixelsPerDip);
    internal static void Finite(params double[] values)
    {
        if (values.Any(v => !double.IsFinite(v))) throw new ArgumentException("Coordinates and dimensions must be finite.");
    }
    internal static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
    internal static Brush Copy(Brush brush, Dictionary<Brush, Brush> cache)
    {
        ArgumentNullException.ThrowIfNull(brush);
        if (cache.TryGetValue(brush, out var copy)) return copy;
        if (brush.IsFrozen) copy = brush;
        else
        {
            brush.VerifyAccess();
            copy = brush.CloneCurrentValue();
            if (!copy.CanFreeze) throw new ArgumentException("Drawing brushes must support freezing.", nameof(brush));
            copy.Freeze();
        }
        cache.Add(brush, copy);
        return copy;
    }
    internal void ValidateMode(params OverlayScaleMode[] allowed)
    {
        if (!allowed.Contains(ScaleMode)) throw new ArgumentException("Unsupported scale mode for this element.");
    }
    internal Pen Pen(Brush brush, double thickness, double scale) =>
        new(brush, ScaleMode == OverlayScaleMode.None ? thickness : thickness / scale);
}

public sealed record LineElement(Point Start, Point End, Brush Stroke, double Thickness = 1) : DrawingElement
{
    internal override DrawingElement Snapshot(Dictionary<Brush, Brush> brushes)
    {
        Finite(Start.X, Start.Y, End.X, End.Y); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.FixedStroke);
        return this with { Stroke = Copy(Stroke, brushes) };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip) =>
        context.DrawLine(Pen(Stroke, Thickness, scale), Start, End);
}

public sealed record CircleElement(Point Center, double Radius, Brush Stroke, double Thickness = 1, Brush? Fill = null) : DrawingElement
{
    internal override DrawingElement Snapshot(Dictionary<Brush, Brush> brushes)
    {
        Finite(Center.X, Center.Y); Positive(Radius, nameof(Radius)); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.FixedStroke, OverlayScaleMode.FixedSize);
        return this with { Stroke = Copy(Stroke, brushes), Fill = Fill == null ? null : Copy(Fill, brushes) };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip)
    {
        double radius = ScaleMode == OverlayScaleMode.FixedSize ? Radius / scale : Radius;
        context.DrawEllipse(Fill, Pen(Stroke, Thickness, scale), Center, radius, radius);
    }
}

public sealed record RectangleElement(Rect Bounds, Brush Stroke, double Thickness = 1, Brush? Fill = null) : DrawingElement
{
    internal override DrawingElement Snapshot(Dictionary<Brush, Brush> brushes)
    {
        Finite(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.FixedStroke);
        return this with { Stroke = Copy(Stroke, brushes), Fill = Fill == null ? null : Copy(Fill, brushes) };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip) =>
        context.DrawRectangle(Fill, Pen(Stroke, Thickness, scale), Bounds);
}

public sealed record CrosshairElement : DrawingElement
{
    public Point Center { get; init; }
    public Brush Stroke { get; init; }
    /// <summary>Half-length of each crosshair arm.</summary>
    public double Size { get; init; }
    public double Thickness { get; init; }
    public CrosshairElement(Point center, Brush stroke, double size = 20, double thickness = 2)
    { Center = center; Stroke = stroke; Size = size; Thickness = thickness; ScaleMode = OverlayScaleMode.FixedSize; }
    internal override DrawingElement Snapshot(Dictionary<Brush, Brush> brushes)
    {
        Finite(Center.X, Center.Y); Positive(Size, nameof(Size)); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.FixedStroke, OverlayScaleMode.FixedSize);
        return this with { Stroke = Copy(Stroke, brushes) };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip)
    {
        double size = ScaleMode == OverlayScaleMode.FixedSize ? Size / scale : Size;
        var pen = Pen(Stroke, Thickness, scale);
        context.DrawLine(pen, new(Center.X - size, Center.Y), new(Center.X + size, Center.Y));
        context.DrawLine(pen, new(Center.X, Center.Y - size), new(Center.X, Center.Y + size));
        context.DrawEllipse(null, pen, Center, size / 2, size / 2);
    }
}

public sealed record TextElement : DrawingElement
{
    public Point Anchor { get; init; }
    public string Text { get; init; }
    public Brush Foreground { get; init; }
    public double FontSize { get; init; }
    public Vector Offset { get; init; }
    public string FontFamily { get; init; } = "Segoe UI";
    public TextElement(Point anchor, string text, Brush foreground, double fontSize = 14, Vector offset = default)
    { Anchor = anchor; Text = text; Foreground = foreground; FontSize = fontSize; Offset = offset; ScaleMode = OverlayScaleMode.AnchoredLabel; }
    internal override DrawingElement Snapshot(Dictionary<Brush, Brush> brushes)
    {
        Finite(Anchor.X, Anchor.Y, Offset.X, Offset.Y); Positive(FontSize, nameof(FontSize));
        ArgumentNullException.ThrowIfNull(Text); ArgumentException.ThrowIfNullOrWhiteSpace(FontFamily);
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.AnchoredLabel);
        return this with { Foreground = Copy(Foreground, brushes) };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip)
    {
        var text = new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily), FontSize, Foreground, pixelsPerDip);
        double factor = ScaleMode == OverlayScaleMode.AnchoredLabel ? 1 / scale : 1;
        context.PushTransform(new MatrixTransform(factor, 0, 0, factor, Anchor.X + Offset.X * factor, Anchor.Y + Offset.Y * factor));
        context.DrawText(text, new Point());
        context.Pop();
    }
}
