using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Controls;
using System.Windows;
using System.Windows.Input;

namespace Fizzy.ImageViewer.Interaction;

/// <summary>Adapts WPF input and pointer effects without owning interaction state.</summary>
internal sealed class ViewerInputBinding(ImageLayer input, MeasurementOverlay overlay, IMouseCapture? capture = null) : IDisposable
{
    private readonly MouseCaptureSession _capture = new(capture ?? new ElementMouseCapture(overlay.Canvas));
    private InteractionCoordinator? _coordinator;
    internal void Connect(InteractionCoordinator coordinator)
    {
        if (_coordinator != null) throw new InvalidOperationException("Input is already connected.");
        _coordinator = coordinator;
        input.Container.Focusable = true;
        input.ImageMouseDown += coordinator.ImageDown;
        input.ImageMouseMove += coordinator.ImageMove;
        input.Container.PreviewKeyDown += KeyDown;
        overlay.Canvas.KeyDown += KeyDown;
        overlay.Canvas.MouseLeftButtonDown += MouseDown;
        overlay.Canvas.MouseMove += MouseMove;
        overlay.Canvas.MouseLeftButtonUp += MouseUp;
        overlay.Canvas.LostMouseCapture += LostCapture;
    }
    internal bool Capture() => _capture.Begin();
    internal void EndPan() => input.EndPan();
    internal void ShowMeasurementCursor() { input.Container.Cursor = Cursors.Pen; input.Container.Focus(); }
    internal void ShowDragCursor() => input.Container.Cursor = Cursors.Hand;
    internal void ShowDefaultCursor() => input.Container.Cursor = Cursors.Cross;
    internal void Restore()
    {
        _capture.End();
        input.EndPan();
        ShowDefaultCursor();
    }
    private void KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _coordinator!.Cancel(); e.Handled = true; }
        else if (e.Key == Key.Delete && _coordinator!.SelectedMeasurement != null) { _coordinator.DeleteSelected(); e.Handled = true; }
    }
    private Point ImagePoint(MouseEventArgs e) => input.ContainerToImage(e.GetPosition(input.Container));
    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = _coordinator!.PointerDown(e.OriginalSource as FrameworkElement, ImagePoint(e));
    }
    private void MouseMove(object sender, MouseEventArgs e)
    {
        if (_coordinator!.UpdateDrag(ImagePoint(e))) e.Handled = true;
    }
    private void MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_coordinator!.EndDrag()) e.Handled = true;
    }
    private void LostCapture(object sender, MouseEventArgs e)
    {
        if (_capture.Lost()) _coordinator!.LostCapture();
    }
    public void Dispose()
    {
        if (_coordinator == null) return;
        Restore();
        input.ImageMouseDown -= _coordinator.ImageDown;
        input.ImageMouseMove -= _coordinator.ImageMove;
        input.Container.PreviewKeyDown -= KeyDown;
        overlay.Canvas.KeyDown -= KeyDown;
        overlay.Canvas.MouseLeftButtonDown -= MouseDown;
        overlay.Canvas.MouseMove -= MouseMove;
        overlay.Canvas.MouseLeftButtonUp -= MouseUp;
        overlay.Canvas.LostMouseCapture -= LostCapture;
        _coordinator = null;
    }
}
