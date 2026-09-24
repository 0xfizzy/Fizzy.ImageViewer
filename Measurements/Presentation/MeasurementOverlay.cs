using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Measurements.Presentation;

/// <summary>WPF display, hit testing and selection appearance. Session policy belongs to the coordinator.</summary>
internal class MeasurementOverlay : UserControl
{
    private readonly Canvas _canvas;
    private double _currentScale = 1;
    private UIElement? _selectedVisual;
    private readonly List<UIElement> _selectionVisuals = [];
    internal Canvas Canvas => _canvas;
    internal event Action<UIElement>? VisualAdded;
    public event Action<UIElement>? VisualRemoved;

    internal MeasurementOverlay()
    {
        _canvas = new Canvas { ClipToBounds = false, IsHitTestVisible = true, Background = null, Focusable = true };
        Content = _canvas;
    }
    public void BindTransform(Transform transform) => _canvas.RenderTransform = transform;
    internal void SetSelection(UIElement? primary, IEnumerable<UIElement> visuals)
    {
        foreach (var visual in _selectionVisuals) ApplySelectionStyle(visual, false);
        _selectionVisuals.Clear(); _selectedVisual = primary;
        foreach (var visual in visuals) { _selectionVisuals.Add(visual); ApplySelectionStyle(visual, true); }
        if (primary != null) _canvas.Focus();
    }
    private static void ApplySelectionStyle(UIElement element, bool selected)
    {
        if (MeasurementVisualData.Get(element) is not { } data) return;
        var brush = selected ? data.SelectedBrush : data.OriginalBrush;
        if (element is Shape shape)
        {
            if (data.UsesFill) shape.Fill = brush;
            else shape.Stroke = brush;
        }
        else if (element is TextBlock text) text.Foreground = brush;
    }
    internal void AddVisual(UIElement shape)
    {
        if (_canvas.Children.Contains(shape)) return;
        ApplyScaleToVisual(shape, _currentScale); _canvas.Children.Add(shape);
        VisualAdded?.Invoke(shape);
    }
    internal void RemoveVisual(UIElement shape)
    {
        if (!_canvas.Children.Contains(shape)) return;
        if (ReferenceEquals(_selectedVisual, shape)) SetSelection(null, []);
        // Remove before notifying observers, so re-entrant removal is harmless.
        _canvas.Children.Remove(shape);
        VisualRemoved?.Invoke(shape);
    }
    internal void ClearVisuals()
    {
        SetSelection(null, []);
        foreach (var shape in _canvas.Children.Cast<UIElement>().ToArray()) RemoveVisual(shape);
    }
    internal void UpdateAnchor(UIElement element, Point newAnchor)
    {
        if (element is FrameworkElement fe && MeasurementVisualData.Get(fe) is { } data)
        {
            data.AnchorPoint = newAnchor;
            ApplyScaleToVisual(fe, _currentScale);
        }
    }

    // === 响应缩放 ===

    public void UpdateScale(double scale)
    {
        _currentScale = scale;
        foreach (UIElement child in _canvas.Children)
        {
            ApplyScaleToVisual(child, scale);
        }
    }

    private void ApplyScaleToVisual(UIElement shape, double scale)
    {
        if (shape is not FrameworkElement element || MeasurementVisualData.Get(element) is not { } data) return;

        data.CaptureAppearance(element);
        var transform = data;

        switch (transform.Mode)
        {
            case OverlayScaleMode.FixedStroke:
                if (shape is Shape s)
                    s.StrokeThickness = data.StrokeThickness / scale;
                Canvas.SetLeft(element, transform.AnchorPoint.X);
                Canvas.SetTop(element, transform.AnchorPoint.Y);
                break;

            case OverlayScaleMode.FixedSize:
            case OverlayScaleMode.AnchoredLabel:
                // Invert the complete visual, including text, padding and background.
                // Its image anchor remains in canvas coordinates; only the screen
                // offset is converted into image units.
                var inverseScale = 1.0 / scale;
                if (transform.CachedScaleTransform == null)
                {
                    element.RenderTransformOrigin = new Point(0, 0);
                    transform.CachedScaleTransform = new ScaleTransform(inverseScale, inverseScale);
                    element.RenderTransform = transform.CachedScaleTransform;
                }
                else
                {
                    transform.CachedScaleTransform.ScaleX = inverseScale;
                    transform.CachedScaleTransform.ScaleY = inverseScale;
                }
                var offset = transform.Mode == OverlayScaleMode.AnchoredLabel
                    ? transform.ScreenOffset : new Vector();
                Canvas.SetLeft(element, transform.AnchorPoint.X + offset.X * inverseScale);
                Canvas.SetTop(element, transform.AnchorPoint.Y + offset.Y * inverseScale);
                break;
        }
    }

}
