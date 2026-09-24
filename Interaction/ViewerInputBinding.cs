using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Viewport;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fizzy.ImageViewer.Interaction;

/// <summary>Adapts WPF input and pointer effects without owning interaction state.</summary>
internal sealed class ViewerInputBinding(ImageViewport input, MeasurementOverlay overlay, MeasurementCollection measurements, LayerCollection layers,
    IMouseCapture? capture = null, ILogger? logger = null) : IInteractionView
{
    private readonly MouseCaptureSession _capture = new(capture ?? new ElementMouseCapture(overlay.Canvas));
    private InteractionCoordinator? _coordinator;
    internal void Connect(InteractionCoordinator coordinator)
    {
        if (_coordinator != null) throw new InvalidOperationException("Input is already connected.");
        _coordinator = coordinator;
        input.Container.Focusable = true;
        input.ImageMouseDown += ImageDown;
        input.ImageMouseMove += ImageMove;
        input.Container.PreviewKeyDown += KeyDown;
        overlay.Canvas.KeyDown += KeyDown;
        overlay.Canvas.MouseLeftButtonDown += MouseDown;
        overlay.Canvas.MouseMove += MouseMove;
        overlay.Canvas.MouseLeftButtonUp += MouseUp;
        overlay.Canvas.LostMouseCapture += LostCapture;
    }
    public double ImageScale => layers.Scale;
    public void SetLayerInputSuppressed(bool suppressed) => layers.SuppressInput(suppressed);
    public bool Capture() => _capture.Begin();
    public void EndPan() => input.EndPan();
    public void ShowMeasurementCursor() { input.Container.Cursor = Cursors.Pen; input.Container.Focus(); }
    public void ShowDragCursor() => input.Container.Cursor = Cursors.Hand;
    public void ShowDefaultCursor() => input.Container.Cursor = Cursors.Cross;
    public void SetSelection(MeasurementItem? item)
        => overlay.SetSelection(item?.Presentation.PrimaryVisual, item?.Presentation.Visuals ?? []);
    public void Restore()
    {
        _capture.End();
        input.EndPan();
        ShowDefaultCursor();
    }
    // Routed UI events have no caller able to observe a thrown callback exception.
    // Keep the dispatcher alive; direct APIs retain their exception contracts.
    private void Dispatch(Action action)
    {
        try { action(); }
        catch (Exception error) { (logger ?? NullLogger.Instance).LogWarning(error, "Measurement input failed"); }
    }
    private void ImageDown(double x, double y) => Dispatch(() => _coordinator?.ImageDown(x, y));
    private void ImageMove(double x, double y) => Dispatch(() => _coordinator?.ImageMove(x, y));
    private void KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Dispatch(() => _coordinator!.Cancel()); }
        else if (e.Key == Key.Delete && _coordinator!.SelectedMeasurement != null) { e.Handled = true; Dispatch(() => _coordinator.DeleteSelected()); }
    }
    private Point ImagePoint(MouseEventArgs e) => input.ContainerToImage(e.GetPosition(input.Container));
    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        Dispatch(() => e.Handled = _coordinator!.PointerDown(measurements.Find(e.OriginalSource as UIElement), ImagePoint(e)));
    }
    private void MouseMove(object sender, MouseEventArgs e)
    {
        Dispatch(() => { if (_coordinator!.UpdateDrag(ImagePoint(e))) e.Handled = true; });
    }
    private void MouseUp(object sender, MouseButtonEventArgs e)
    {
        Dispatch(() => { if (_coordinator!.EndDrag()) e.Handled = true; });
    }
    private void LostCapture(object sender, MouseEventArgs e)
    {
        Dispatch(() => { if (_capture.Lost()) _coordinator!.LostCapture(); });
    }
    public void Dispose()
    {
        if (_coordinator == null) return;
        Restore();
        input.ImageMouseDown -= ImageDown;
        input.ImageMouseMove -= ImageMove;
        input.Container.PreviewKeyDown -= KeyDown;
        overlay.Canvas.KeyDown -= KeyDown;
        overlay.Canvas.MouseLeftButtonDown -= MouseDown;
        overlay.Canvas.MouseMove -= MouseMove;
        overlay.Canvas.MouseLeftButtonUp -= MouseUp;
        overlay.Canvas.LostMouseCapture -= LostCapture;
        _coordinator = null;
    }
}
