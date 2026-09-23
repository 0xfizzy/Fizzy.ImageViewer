using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Immutable image-coordinate drawing data. Brushes are snapshotted when submitted.</summary>
public abstract record DrawingElement
{
    private protected DrawingElement() { }
    public OverlayScaleMode ScaleMode { get; init; } = OverlayScaleMode.FixedStroke;
    internal abstract DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes);
    internal abstract void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources);
    internal static void Finite(double a, double b, double c = 0, double d = 0)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c) || !double.IsFinite(d)) throw new ArgumentException("Coordinates and dimensions must be finite.");
    }
    internal static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
    internal void ValidateMode(OverlayScaleMode a, OverlayScaleMode b, OverlayScaleMode? c = null)
    {
        if (ScaleMode != a && ScaleMode != b && ScaleMode != c) throw new ArgumentException("Unsupported scale mode for this element.");
    }
    internal Pen Pen(Brush brush, double thickness, double scale, DrawingResources resources) =>
        resources.GetPen(brush, ScaleMode == OverlayScaleMode.None ? thickness : thickness / scale);
}

public sealed record LineElement(Point Start, Point End, Brush Stroke, double Thickness = 1) : DrawingElement
{
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Start.X, Start.Y, End.X, End.Y); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.FixedStroke);
        var stroke = BrushSnapshots.Copy(Stroke, ref brushes);
        return ReferenceEquals(stroke, Stroke) ? this : this with { Stroke = stroke };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources) =>
        context.DrawLine(Pen(Stroke, Thickness, scale, resources), Start, End);
}

public sealed record CircleElement(Point Center, double Radius, Brush Stroke, double Thickness = 1, Brush? Fill = null) : DrawingElement
{
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Center.X, Center.Y); Positive(Radius, nameof(Radius)); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.FixedStroke, OverlayScaleMode.FixedSize);
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

public sealed record RectangleElement(Rect Bounds, Brush Stroke, double Thickness = 1, Brush? Fill = null) : DrawingElement
{
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.FixedStroke);
        var stroke = BrushSnapshots.Copy(Stroke, ref brushes);
        var fill = Fill == null ? null : BrushSnapshots.Copy(Fill, ref brushes);
        return ReferenceEquals(stroke, Stroke) && ReferenceEquals(fill, Fill)
            ? this : this with { Stroke = stroke, Fill = fill };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources) =>
        context.DrawRectangle(Fill, Pen(Stroke, Thickness, scale, resources), Bounds);
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
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Center.X, Center.Y); Positive(Size, nameof(Size)); Positive(Thickness, nameof(Thickness));
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.FixedStroke, OverlayScaleMode.FixedSize);
        var stroke = BrushSnapshots.Copy(Stroke, ref brushes);
        return ReferenceEquals(stroke, Stroke) ? this : this with { Stroke = stroke };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources)
    {
        double size = ScaleMode == OverlayScaleMode.FixedSize ? Size / scale : Size;
        var pen = Pen(Stroke, Thickness, scale, resources);
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
    internal override DrawingElement Snapshot(ref Dictionary<Brush, Brush>? brushes)
    {
        Finite(Anchor.X, Anchor.Y, Offset.X, Offset.Y); Positive(FontSize, nameof(FontSize));
        ArgumentNullException.ThrowIfNull(Text); ArgumentException.ThrowIfNullOrWhiteSpace(FontFamily);
        ValidateMode(OverlayScaleMode.None, OverlayScaleMode.AnchoredLabel);
        var foreground = BrushSnapshots.Copy(Foreground, ref brushes);
        return ReferenceEquals(foreground, Foreground) ? this : this with { Foreground = foreground };
    }
    internal override void Draw(DrawingContext context, double scale, double pixelsPerDip, DrawingResources resources)
    {
        var text = new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily), FontSize, Foreground, pixelsPerDip);
        double factor = ScaleMode == OverlayScaleMode.AnchoredLabel ? 1 / scale : 1;
        context.PushTransform(new MatrixTransform(factor, 0, 0, factor, Anchor.X + Offset.X * factor, Anchor.Y + Offset.Y * factor));
        context.DrawText(text, new Point());
        context.Pop();
    }
}
