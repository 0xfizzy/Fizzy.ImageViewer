using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer.Controls
{
    public class OverlayLayer : UserControl
    {
        private readonly Canvas _canvas;
        private double _currentScale = 1.0;
        private UIElement? _selectedShape;
        private Managers.EditManager? _editManager;

        internal Canvas Canvas => _canvas;

        public OverlayLayer()
        {
            _canvas = new Canvas
            {
                ClipToBounds = false,
                IsHitTestVisible = true,
                Background = null, // 不设背景，避免拦截鼠标事件到 ImageLayer
                Focusable = true
            };
            Content = _canvas;

            // 绑定事件
            _canvas.MouseLeftButtonDown += OnCanvasMouseDown;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseLeftButtonUp += OnCanvasMouseUp;
            _canvas.KeyDown += OnCanvasKeyDown;
        }

        public void BindTransform(Transform transform)
        {
            _canvas.RenderTransform = transform;
        }

        // === 选择功能 ===

        /// <summary>
        /// 当前选中的形状。
        /// </summary>
        public UIElement? SelectedShape => _selectedShape;

        /// <summary>
        /// 设置是否启用 HitTest（测量时关闭）。
        /// </summary>
        public void SetHitTestEnabled(bool enabled)
        {
            _canvas.IsHitTestVisible = enabled;
            if (!enabled)
            {
                ClearSelection();
            }
        }

        private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Don't allow shape selection during edit mode
            if (_editManager?.IsEditing == true) return;

            // 检查点击的是否是形状（Background=null 时只有子形状能触发此事件）
            if (e.OriginalSource is FrameworkElement fe && fe != _canvas && fe.Tag is OverlayTagData)
            {
                Select(fe);
                e.Handled = true;
            }
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            // Mouse move events are handled by EditManager if in edit mode
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            // Mouse up events are handled by EditManager if in edit mode
        }

        private void OnCanvasKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedShape != null)
            {
                DeleteSelected();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 选中指定形状。
        /// </summary>
        public void Select(UIElement shape)
        {
            if (_selectedShape == shape) return;

            ClearSelection();
            _selectedShape = shape;
            _canvas.Focus(); // 获取焦点以接收键盘事件（Delete 删除）
            ApplySelectionStyle(shape, true);

            // 高亮关联形状
            if (shape is FrameworkElement fe && fe.Tag is OverlayTagData data && data.Selection.LinkedShapes != null)
            {
                foreach (var linked in data.Selection.LinkedShapes)
                {
                    ApplySelectionStyle(linked, true);
                }
            }
        }

        /// <summary>
        /// 取消选中。
        /// </summary>
        public void ClearSelection()
        {
            // Exit edit mode even when editing was started programmatically.
            ExitEditMode();
            if (_selectedShape == null) return;

            ApplySelectionStyle(_selectedShape, false);

            // 恢复关联形状
            if (_selectedShape is FrameworkElement fe && fe.Tag is OverlayTagData data && data.Selection.LinkedShapes != null)
            {
                foreach (var linked in data.Selection.LinkedShapes)
                {
                    ApplySelectionStyle(linked, false);
                }
            }

            _selectedShape = null;
        }

        /// <summary>
        /// 删除当前选中的形状。
        /// </summary>
        public void DeleteSelected()
        {
            if (_selectedShape == null) return;

            var toRemove = _selectedShape;
            ClearSelection();
            RemoveShape(toRemove);
        }

        private static void ApplySelectionStyle(UIElement element, bool selected)
        {
            if (element is not FrameworkElement fe || fe.Tag is not OverlayTagData data) return;

            var brush = selected ? Shapes.SelectedBrush : data.Selection.OriginalBrush;

            switch (element)
            {
                case Shape shape:
                    shape.Stroke = brush;
                    break;
                case TextBlock textBlock:
                    textBlock.Foreground = brush;
                    break;
            }
        }


        // === 事件 ===

        /// <summary>
        /// 形状添加后触发的事件。
        /// </summary>
        public event Action<UIElement, IDisposable>? ShapeAdded;

        /// <summary>
        /// 形状移除前触发的事件。
        /// </summary>
        public event Action<UIElement>? ShapeRemoved;

        /// <summary>
        /// 形状编辑中触发的事件（拖拽控制点时）。
        /// </summary>
        public event Action<UIElement>? ShapeEditing;

        /// <summary>
        /// 形状编辑完成触发的事件（释放控制点后）。
        /// </summary>
        public event Action<UIElement>? ShapeEdited;

        // === Internal API: 供 Viewer 和 MeasureManager 共享 ===

        internal void AddShape(UIElement shape)
        {
            if (!_canvas.Children.Contains(shape))
            {
                ApplyScaleToShape(shape, _currentScale);
                _canvas.Children.Add(shape);

                var handle = new Internal.DrawingHandle(() =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        RemoveShape(shape);
                    });
                });

                ShapeAdded?.Invoke(shape, handle);
            }
        }

        internal void RemoveShape(UIElement shape)
        {
            if (!_canvas.Children.Contains(shape)) return;
            if (ReferenceEquals(_selectedShape, shape)) ClearSelection();

            // 先移除关联形状
            if (shape is FrameworkElement fe && fe.Tag is OverlayTagData data && data.Selection.LinkedShapes != null)
            {
                foreach (var linked in data.Selection.LinkedShapes)
                {
                    if (_canvas.Children.Contains(linked))
                    {
                        InvokeOnRemoved(linked);
                        ShapeRemoved?.Invoke(linked);
                        _canvas.Children.Remove(linked);
                    }
                }
            }

            InvokeOnRemoved(shape);
            ShapeRemoved?.Invoke(shape);
            _canvas.Children.Remove(shape);
        }

        internal void Clear()
        {
            ClearSelection();
            ExitEditMode();
            foreach (var child in _canvas.Children.Cast<UIElement>().ToArray())
                RemoveShape(child);
        }

        private static void InvokeOnRemoved(UIElement shape)
        {
            if (shape is FrameworkElement fe && fe.Tag is OverlayTagData data)
            {
                data.Selection.OnRemoved?.Invoke();
            }
        }

        internal void UpdateAnchor(UIElement element, Point newAnchor)
        {
            if (element is FrameworkElement fe && fe.Tag is OverlayTagData data)
            {
                data.Transform.AnchorPoint = newAnchor;
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
            if (shape is not FrameworkElement element || element.Tag is not OverlayTagData data) return;

            var transform = data.Transform;

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

        // === 编辑模式 ===

        /// <summary>
        /// 设置 EditManager 实例（由 Viewer 初始化时调用）。
        /// </summary>
        internal void SetEditManager(Managers.EditManager editManager)
        {
            _editManager = editManager;

            // Forward edit events
            _editManager.ShapeEditing += shape => ShapeEditing?.Invoke(shape);
            _editManager.ShapeEdited += shape => ShapeEdited?.Invoke(shape);
        }

        /// <summary>
        /// 进入编辑模式。
        /// </summary>
        public void EnterEditMode()
        {
            if (_selectedShape == null || _editManager == null) return;
            _editManager.StartEditing(_selectedShape);
        }

        /// <summary>
        /// 进入编辑模式（指定形状）。
        /// </summary>
        public void EnterEditMode(UIElement shape)
        {
            if (shape == null || _editManager == null) return;
            _editManager.StartEditing(shape);
        }

        /// <summary>
        /// 退出编辑模式。
        /// </summary>
        public void ExitEditMode()
        {
            _editManager?.StopEditing();
        }
    }
}
