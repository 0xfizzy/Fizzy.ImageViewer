using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer;

/// <summary>Private visual metadata. Geometry ownership stays in measurement items.</summary>
internal sealed class OverlayShapeData(OverlayScaleMode mode, ShapeType type)
{
    internal OverlayScaleMode Mode { get; } = mode;
    internal ShapeType ShapeType { get; } = type;
    internal Point AnchorPoint { get; set; }
    internal Vector ScreenOffset { get; set; }
    internal ScaleTransform? CachedScaleTransform { get; set; }
    internal Brush? OriginalBrush { get; set; }
    internal Brush SelectedBrush { get; init; } = Brushes.Yellow;
}
