using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer;

/// <summary>Display-only transform metadata, independent of measurement ownership.</summary>
public sealed class OverlayTransformData
{
    public OverlayScaleMode Mode { get; init; }
    public Point AnchorPoint { get; set; }
    public Vector ScreenOffset { get; set; }
    public ScaleTransform? CachedScaleTransform { get; set; }
}

public sealed class OverlayTagData(OverlayScaleMode mode, ShapeType type)
{
    public OverlayTransformData Transform { get; } = new() { Mode = mode };
    public ShapeType ShapeType { get; } = type;
    public OverlayScaleMode Mode => Transform.Mode;
    public Point AnchorPoint { get => Transform.AnchorPoint; set => Transform.AnchorPoint = value; }
    public Vector ScreenOffset { get => Transform.ScreenOffset; set => Transform.ScreenOffset = value; }
    public Brush? OriginalBrush { get; set; }
}
