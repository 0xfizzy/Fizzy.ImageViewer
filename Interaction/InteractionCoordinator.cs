using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Input;

namespace Fizzy.ImageViewer.Interaction;

internal enum InteractionMode { Idle, Editing, Measuring }

internal interface IMouseCapture
{
    bool IsCaptured { get; }
    bool Capture();
    void Release();
}

internal sealed class OverlayMouseCapture(OverlayLayer overlay) : IMouseCapture
{
    public bool IsCaptured => overlay.Canvas.IsMouseCaptured;
    public bool Capture() => overlay.Canvas.CaptureMouse();
    public void Release() => overlay.Canvas.ReleaseMouseCapture();
}

/// <summary>Owns selection and every input-state transition on the viewer's UI thread.</summary>
internal sealed class InteractionCoordinator : IDisposable
{
    private readonly ImageLayer _input;
    private readonly OverlayLayer _overlay;
    private readonly EditManager _edit;
    private readonly MeasureManager _measure;
    private readonly ViewerLayers _layers;
    private readonly IMouseCapture _capture;
    private bool _disposed;
    public InteractionMode Mode { get; private set; }
    public UIElement? SelectedShape { get; private set; }
    public MeasurementItem? SelectedMeasurement => _measure.Context.Find(SelectedShape);
    internal EditManager Editor => _edit;

    internal InteractionCoordinator(ImageLayer input, OverlayLayer overlay, EditManager edit, MeasureManager measure, ViewerLayers layers, IMouseCapture? capture = null)
    {
        _input = input; _overlay = overlay; _edit = edit; _measure = measure; _layers = layers;
        _capture = capture ?? new OverlayMouseCapture(overlay);
        input.Container.Focusable = true;
        overlay.Coordinator = this;
        layers.CancelMeasurement = Cancel;
        overlay.VisualRemoving += VisualRemoving;
        measure.Context.ItemRemoving += ItemRemoving;
        input.ImageMouseDown += ImageDown; input.ImageMouseMove += ImageMove;
        input.Container.PreviewKeyDown += KeyDown;
        overlay.Canvas.MouseLeftButtonDown += MouseDown;
        overlay.Canvas.MouseMove += MouseMove;
        overlay.Canvas.MouseLeftButtonUp += MouseUp;
        overlay.Canvas.LostMouseCapture += LostCapture;
    }
    internal bool Hit(UIElement shape)
    {
        if (Mode == InteractionMode.Editing) return false;
        if (Mode == InteractionMode.Measuring) return true;
        Select(shape); return true;
    }
    internal void Select(UIElement shape)
    {
        if (_disposed || Mode == InteractionMode.Measuring || !_overlay.Canvas.Children.Contains(shape)) return;
        StopEditing();
        var item = _measure.Context.Find(shape);
        SelectedShape = item?.PrimaryVisual ?? shape;
        _overlay.SetSelection(SelectedShape, item?.Visuals ?? [shape]);
    }
    internal void ClearSelection()
    {
        StopEditing(); SelectedShape = null; _overlay.SetSelection(null, []);
    }
    internal void StartMeasurement(string name)
    {
        if (_disposed || !_measure.HasMethod(name) || !_layers.Measurements.IsVisible) return;
        Cancel(); ClearSelection();
        try
        {
            if (!_measure.Start(name)) return;
            Mode = InteractionMode.Measuring;
            _layers.SuppressInput(true);
            _input.Container.Cursor = Cursors.Pen;
            _input.Container.Focus();
        }
        catch { Cancel(); throw; }
    }
    internal void StartEditing(UIElement shape)
    {
        if (_disposed || !_overlay.Canvas.Children.Contains(shape) || !_edit.CanEdit(shape) ||
            !_layers.Measurements.IsVisible || !_layers.Measurements.IsHitTestVisible) return;
        Cancel(); Select(shape);
        try { if (_edit.StartEditing(SelectedShape!)) Mode = InteractionMode.Editing; }
        catch { Cancel(); throw; }
    }
    internal void StopEditing()
    {
        _edit.StopEditing();
        if (Mode == InteractionMode.Editing) { Mode = InteractionMode.Idle; RestoreInput(); }
    }
    internal void Cancel()
    {
        if (_disposed) return;
        try { _measure.Cancel(); }
        finally { _edit.StopEditing(); Mode = InteractionMode.Idle; RestoreInput(); }
    }
    private void RestoreInput()
    {
        if (_capture.IsCaptured) _capture.Release();
        _input.Container.Cursor = Cursors.Cross;
        _layers.SuppressInput(false);
    }
    internal void DeleteSelected()
    {
        var selected = SelectedShape;
        ClearSelection();
        if (selected != null) _measure.Context.RemoveShape(selected);
    }
    private void ItemRemoving(MeasurementItem item)
    {
        if (ReferenceEquals(SelectedMeasurement, item) || ReferenceEquals(_edit.EditingShape, item.PrimaryVisual)) ClearSelection();
    }
    private void VisualRemoving(UIElement visual)
    {
        if (ReferenceEquals(SelectedShape, visual) || ReferenceEquals(_edit.EditingShape, visual)) ClearSelection();
    }
    internal void ImageDown(double x, double y)
    {
        if (Mode != InteractionMode.Measuring) return;
        var version = _measure.SessionVersion;
        try { if (_measure.Click(new(x, y)) && version == _measure.SessionVersion) { Mode = InteractionMode.Idle; RestoreInput(); } }
        catch { if (version == _measure.SessionVersion) Cancel(); throw; }
    }
    internal void ImageMove(double x, double y)
    {
        if (Mode != InteractionMode.Measuring) return;
        try { _measure.Move(new(x, y)); } catch { Cancel(); throw; }
    }
    private void KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        else if (e.Key == Key.Delete && SelectedShape != null) { DeleteSelected(); e.Handled = true; }
    }
    private Point ImagePoint(MouseEventArgs e) => _input.ContainerToImage(e.GetPosition(_input.Container));
    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = BeginDrag(ImagePoint(e));
    }
    internal bool BeginDrag(Point point)
    {
        if (Mode != InteractionMode.Editing) return false;
        if (!_edit.BeginDrag(point, _layers.Scale)) { StopEditing(); return false; }
        if (!_capture.Capture()) { _edit.EndDrag(); return false; }
        _input.Container.Cursor = Cursors.Hand;
        return true;
    }
    private void MouseMove(object sender, MouseEventArgs e)
    {
        if (!_edit.IsDragging) return;
        try { _edit.UpdateDrag(ImagePoint(e)); e.Handled = true; } catch { Cancel(); throw; }
    }
    private void MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_edit.IsDragging) return;
        try { _edit.EndDrag(); }
        finally { RestoreInput(); e.Handled = true; }
    }
    private void LostCapture(object sender, MouseEventArgs e)
    {
        if (!_edit.IsDragging) return;
        try { _edit.EndDrag(); }
        finally { _input.Container.Cursor = Cursors.Cross; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        try { Cancel(); }
        finally
        {
            ClearSelection(); _disposed = true;
            _overlay.VisualRemoving -= VisualRemoving; _measure.Context.ItemRemoving -= ItemRemoving;
            _input.ImageMouseDown -= ImageDown; _input.ImageMouseMove -= ImageMove;
            _input.Container.PreviewKeyDown -= KeyDown;
            _overlay.Canvas.MouseLeftButtonDown -= MouseDown;
            _overlay.Canvas.MouseMove -= MouseMove; _overlay.Canvas.MouseLeftButtonUp -= MouseUp;
            _overlay.Canvas.LostMouseCapture -= LostCapture;
            _layers.CancelMeasurement = null;
            _overlay.Coordinator = null;
        }
    }
}
