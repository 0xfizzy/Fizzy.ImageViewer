using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Enums;
using System;
using System.Windows;
using System.Windows.Input;

namespace Fizzy.ImageViewer.Managers;

/// <summary>
/// Manages shape editing mode - rendering control points, handling drag interactions.
/// </summary>
public class EditManager
{
    private readonly ImageLayer _inputLayer;
    private readonly OverlayLayer _outputLayer;

    private UIElement? _editingShape;
    private IShapeEditor? _currentEditor;
    private int _draggingPointIndex = -1;
    private bool _isDragging;

    public bool IsEditing => _editingShape != null;

    public event Action<UIElement>? ShapeEditing;
    public event Action<UIElement>? ShapeEdited;

    public EditManager(ImageLayer inputLayer, OverlayLayer outputLayer)
    {
        _inputLayer = inputLayer;
        _outputLayer = outputLayer;

        // Only subscribe to OverlayLayer events - simpler and more reliable
        // This ensures all mouse events during edit mode are handled consistently
        _outputLayer.Canvas.MouseLeftButtonDown += OnMouseDown;
        _outputLayer.Canvas.MouseMove += OnMouseMove;
        _outputLayer.Canvas.MouseLeftButtonUp += OnMouseUp;
    }

    /// <summary>
    /// Enter edit mode for the specified shape.
    /// </summary>
    public void StartEditing(UIElement shape)
    {
        if (_editingShape == shape) return;

        StopEditing();

        _editingShape = shape;

        if (shape is not FrameworkElement fe || fe.Tag is not OverlayTagData data)
            return;

        // Create appropriate editor based on shape type
        _currentEditor = CreateEditor(data.ShapeType);
        if (_currentEditor == null) return;

        data.Edit.Editor = _currentEditor;

        // Render control points
        var controlPoints = _currentEditor.GetControlPoints(shape);
        for (int i = 0; i < controlPoints.Count; i++)
        {
            var handle = ControlPointHandle.CreateHandle(controlPoints[i], shape, i);
            data.Edit.ControlPointHandles.Add(handle);
            _outputLayer.AddShape(handle);
        }

        // Note: We do NOT disable hit testing on OverlayLayer during edit mode
        // because control points need to receive mouse events for dragging.
        // Shape selection is prevented by checking IsEditing state instead.
    }

    /// <summary>
    /// Exit edit mode, remove control points.
    /// </summary>
    public void StopEditing()
    {
        if (_editingShape == null) return;

        // Remove control point handles
        if (_editingShape is FrameworkElement fe && fe.Tag is OverlayTagData data)
        {
            foreach (var handle in data.Edit.ControlPointHandles)
            {
                _outputLayer.RemoveShape(handle);
            }
            data.Edit.ControlPointHandles.Clear();
            data.Edit.Editor = null;
        }

        _editingShape = null;
        _currentEditor = null;
        _isDragging = false;
        _draggingPointIndex = -1;

        // Note: We do NOT re-enable hit testing here because we never disabled it
    }

    private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsEditing) return;

        // Convert container coordinates to image coordinates
        var posContainer = e.GetPosition(_inputLayer.Container);
        var imagePoint = _inputLayer.ContainerToImage(posContainer);

        // Check if clicking on a control point handle
        if (_editingShape is FrameworkElement fe && fe.Tag is OverlayTagData data)
        {
            for (int i = 0; i < data.Edit.ControlPointHandles.Count; i++)
            {
                var handle = data.Edit.ControlPointHandles[i];
                if (IsPointNearHandle(imagePoint, handle))
                {
                    _isDragging = true;
                    _draggingPointIndex = i;
                    _inputLayer.Container.Cursor = Cursors.Hand;

                    // Capture mouse to receive all events during drag
                    _outputLayer.Canvas.CaptureMouse();

                    // Mark as handled to prevent bubbling
                    e.Handled = true;
                    return;
                }
            }
        }

        // Click outside control points - exit edit mode
        StopEditing();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!IsEditing || !_isDragging || _currentEditor == null || _editingShape == null)
            return;

        // Convert container coordinates to image coordinates
        var posContainer = e.GetPosition(_inputLayer.Container);
        var imagePoint = _inputLayer.ContainerToImage(posContainer);

        // Update shape geometry
        _currentEditor.UpdateControlPoint(_editingShape, _draggingPointIndex, imagePoint);
        if (_editingShape is FrameworkElement { Tag: OverlayTagData geometry }) geometry.GeometryVersion++;
        _currentEditor.UpdateLinkedShapes(_editingShape);

        // Update ALL control point handle positions to match the new shape geometry
        if (_editingShape is FrameworkElement fe && fe.Tag is OverlayTagData data)
        {
            var controlPoints = _currentEditor.GetControlPoints(_editingShape);
            for (int i = 0; i < controlPoints.Count && i < data.Edit.ControlPointHandles.Count; i++)
            {
                var handle = data.Edit.ControlPointHandles[i];

                // Ensure handle is still in the canvas (defensive check)
                if (!_outputLayer.Canvas.Children.Contains(handle))
                {
                    _outputLayer.Canvas.Children.Add(handle);
                }

                // Ensure handle is on top (highest z-index)
                System.Windows.Controls.Panel.SetZIndex(handle, 1000);

                _outputLayer.UpdateAnchor(handle, controlPoints[i]);
            }

            // Force immediate visual update
            _outputLayer.Canvas.UpdateLayout();
        }

        // Fire editing event
        ShapeEditing?.Invoke(_editingShape);

        // Mark as handled
        e.Handled = true;
    }

    private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsEditing || !_isDragging) return;

        _isDragging = false;
        _draggingPointIndex = -1;
        _inputLayer.Container.Cursor = Cursors.Cross;

        // Always release capture
        if (_outputLayer.Canvas.IsMouseCaptured)
        {
            _outputLayer.Canvas.ReleaseMouseCapture();
        }

        // Fire edited event
        if (_editingShape != null)
        {
            ShapeEdited?.Invoke(_editingShape);
        }

        // Mark as handled
        e.Handled = true;
    }

    private bool IsPointNearHandle(Point point, UIElement handle)
    {
        if (handle is not FrameworkElement fe || fe.Tag is not OverlayTagData data)
            return false;

        var handlePos = data.AnchorPoint;
        double distance = Math.Sqrt(Math.Pow(point.X - handlePos.X, 2) + Math.Pow(point.Y - handlePos.Y, 2));

        // Hit test threshold (in image coordinates)
        return distance < 10.0;
    }

    private static IShapeEditor? CreateEditor(ShapeType shapeType)
    {
        return shapeType switch
        {
            ShapeType.Line => new LineEditor(),
            ShapeType.Rectangle => new RectangleEditor(),
            ShapeType.Circle => new CircleEditor(),
            ShapeType.Point => new PointEditor(),
            ShapeType.Crosshair => new CrosshairEditor(),
            _ => null
        };
    }
}
