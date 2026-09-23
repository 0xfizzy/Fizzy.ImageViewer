using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer.Controls;

/// <summary>WPF display, hit testing and selection appearance. Session policy belongs to the coordinator.</summary>
internal class OverlayLayer : UserControl
{
    private readonly Canvas _canvas;
    private double _currentScale = 1;
    private UIElement? _selectedShape;
    private readonly List<UIElement> _selectionVisuals = [];
    internal Canvas Canvas => _canvas;
    internal event Action<UIElement>? VisualRemoving;
    internal event Action<UIElement>? VisualAdded;
    public event Action<UIElement>? ShapeRemoved;

    internal OverlayLayer()
    {
        _canvas = new Canvas { ClipToBounds = false, IsHitTestVisible = true, Background = null, Focusable = true };
        Content = _canvas;
    }
    public void BindTransform(Transform transform) => _canvas.RenderTransform = transform;
    internal void SetSelection(UIElement? primary, IEnumerable<UIElement> visuals)
    {
        foreach (var visual in _selectionVisuals) ApplySelectionStyle(visual, false);
        _selectionVisuals.Clear(); _selectedShape = primary;
        foreach (var visual in visuals) { _selectionVisuals.Add(visual); ApplySelectionStyle(visual, true); }
        if (primary != null) _canvas.Focus();
    }
    private static void ApplySelectionStyle(UIElement element, bool selected)
    {
        if (element is not FrameworkElement { Tag: OverlayShapeData data }) return;
        var brush = selected ? data.SelectedBrush : data.OriginalBrush;
        if (element is Shape shape) shape.Stroke = brush;
        else if (element is TextBlock text) text.Foreground = brush;
    }
    internal void AddShape(UIElement shape)
    {
        if (_canvas.Children.Contains(shape)) return;
        ApplyScaleToShape(shape, _currentScale); _canvas.Children.Add(shape);
        VisualAdded?.Invoke(shape);
    }
    internal void RemoveVisual(UIElement shape)
    {
        if (!_canvas.Children.Contains(shape)) return;
        try { VisualRemoving?.Invoke(shape); }
        finally
        {
            if (ReferenceEquals(_selectedShape, shape)) SetSelection(null, []);
            // Remove before notifying observers, so re-entrant removal is harmless.
            _canvas.Children.Remove(shape);
            ShapeRemoved?.Invoke(shape);
        }
    }
    internal void ClearVisuals()
    {
        SetSelection(null, []);
        foreach (var shape in _canvas.Children.Cast<UIElement>().ToArray()) RemoveVisual(shape);
    }
    internal void UpdateAnchor(UIElement element, Point newAnchor)
    {
        if (element is FrameworkElement fe && fe.Tag is OverlayShapeData data)
        {
            data.AnchorPoint = newAnchor;
            ApplyScaleToShape(fe, _currentScale);
        }
    }

    // === 响应缩放 ===

    public void UpdateScale(double scale)
    {
        _currentScale = scale;
        foreach (UIElement child in _canvas.Children)
        {
            ApplyScaleToShape(child, scale);
        }
    }

    private void ApplyScaleToShape(UIElement shape, double scale)
    {
        if (shape is not FrameworkElement element || element.Tag is not OverlayShapeData data) return;

        var transform = data;

        switch (transform.Mode)
        {
            case OverlayScaleMode.FixedStroke:
                if (shape is Shape s)
                    s.StrokeThickness = Shapes.BaseStrokeThickness / scale;
                Canvas.SetLeft(element, transform.AnchorPoint.X);
                Canvas.SetTop(element, transform.AnchorPoint.Y);
                break;

            case OverlayScaleMode.FixedSize:
                var inverseScale = 1.0 / scale;

                // 使用缓存的 ScaleTransform，避免每次类型检查和创建
                if (transform.CachedScaleTransform == null)
                {
                    // 几何中心在 (0,0)，逆缩放必须围绕原点，否则放大时点标记会偏移
                    element.RenderTransformOrigin = new Point(0, 0);
                    transform.CachedScaleTransform = new ScaleTransform(inverseScale, inverseScale);
                    element.RenderTransform = transform.CachedScaleTransform;
                }
                else
                {
                    transform.CachedScaleTransform.ScaleX = inverseScale;
                    transform.CachedScaleTransform.ScaleY = inverseScale;
                }

                Canvas.SetLeft(element, transform.AnchorPoint.X);
                Canvas.SetTop(element, transform.AnchorPoint.Y);
                break;

            case OverlayScaleMode.AnchoredLabel:
                if (shape is TextBlock t)
                {
                    double newSize = Shapes.BaseFontSize / scale;
                    t.FontSize = Math.Max(1, newSize);
                    double finalX = transform.AnchorPoint.X + (transform.ScreenOffset.X / scale);
                    double finalY = transform.AnchorPoint.Y + (transform.ScreenOffset.Y / scale);
                    Canvas.SetLeft(t, finalX);
                    Canvas.SetTop(t, finalY);
                }
                break;
        }
    }

}
