using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Controls;

/// <summary>Private visual metadata. Geometry ownership stays in measurement items.</summary>
internal sealed class OverlayShapeData(OverlayScaleMode mode, bool usesFill = false)
{
    private static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "Data", typeof(OverlayShapeData), typeof(OverlayShapeData));
    internal static OverlayShapeData? Get(DependencyObject visual) => (OverlayShapeData?)visual.GetValue(DataProperty);
    internal static void Attach(FrameworkElement visual, OverlayShapeData data) => visual.SetValue(DataProperty, data);

    private bool _appearanceCaptured;
    internal double StrokeThickness { get; private set; }
    internal double FontSize { get; private set; }
    internal void CaptureAppearance(FrameworkElement visual)
    {
        if (_appearanceCaptured) return;
        _appearanceCaptured = true;
        if (visual is System.Windows.Shapes.Shape shape)
        {
            StrokeThickness = shape.StrokeThickness;
            OriginalBrush = UsesFill ? shape.Fill : shape.Stroke;
        }
        if (visual is System.Windows.Controls.TextBlock text)
        {
            FontSize = text.FontSize;
            OriginalBrush = text.Foreground;
        }
        if (OriginalBrush != null)
        {
            Dictionary<Brush, Brush>? cache = null;
            OriginalBrush = BrushSnapshots.Copy(OriginalBrush, ref cache);
        }
    }
    internal OverlayScaleMode Mode { get; } = mode;
    internal bool UsesFill { get; } = usesFill;
    internal Point AnchorPoint { get; set; }
    internal Vector ScreenOffset { get; set; }
    internal ScaleTransform? CachedScaleTransform { get; set; }
    internal Brush? OriginalBrush { get; private set; }
    internal Brush SelectedBrush { get; init; } = Brushes.Yellow;
}
