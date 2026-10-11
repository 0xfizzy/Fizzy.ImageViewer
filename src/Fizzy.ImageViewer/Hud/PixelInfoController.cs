using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Frames;
using System.Windows;
using System.Windows.Input;

namespace Fizzy.ImageViewer.Hud;

internal sealed class PixelInfoController : IDisposable, IFrameQueryClient
{
    private readonly ImageViewport _image;
    private readonly PixelQueryScheduler _scheduler;
    private readonly PixelInfoState _state;
    private readonly Func<Point?> _readPointer;
    private QuerySubscription? _subscription;
    private bool _enabled;
    public bool IsEnabled
    {
        get => _enabled;
        set { if (value) Enable(); else Disable(); }
    }

    internal PixelInfoController(ImageViewport image, HudLayer hud, PixelQueryScheduler scheduler,
        Func<Point?>? readPointer = null)
    {
        _image = image;
        _scheduler = scheduler;
        _readPointer = readPointer ?? ReadPointer;
        _state = new(text =>
        {
            if (text != null) hud.UpdatePixelInfo(text);
            hud.SetPixelInfoVisible(text != null);
        });
    }

    public void Enable()
    {
        if (IsEnabled) return;
        _subscription = _scheduler.Register(this);
        _enabled = true;
        _state.Enable();
        _image.ImageMouseMove += Move;
        _image.InputSurface.MouseLeave += Leave;
    }

    public void Disable()
    {
        if (!IsEnabled) return;
        _enabled = false;
        _state.Disable();
        _image.ImageMouseMove -= Move;
        _image.InputSurface.MouseLeave -= Leave;
        _subscription?.Dispose();
        _subscription = null;
    }
    private void Move(double x, double y) => _state.Move(x, y);
    private void Leave(object sender, MouseEventArgs e) => _state.Leave();

    QueryPolicy IFrameQueryClient.Policy => _state.Policy;
    QueryRequest? IFrameQueryClient.Capture(FrameDescriptor descriptor)
    {
        // Resolve against current layout on every scheduler capture: fit, resize and
        // layout movement can change the pixel under a stationary screen pointer.
        if (_readPointer() is { } pointer)
        {
            var imagePoint = _image.ContainerToImage(pointer);
            return _state.Capture(descriptor, imagePoint.X, imagePoint.Y);
        }
        _state.Leave();
        return _state.Capture(descriptor);
    }

    void IFrameQueryClient.ClearResult() => _state.ClearResult();
    void IFrameQueryClient.InvalidateResult(ResultInvalidation reason) => _state.InvalidateResult(reason);

    private Point? ReadPointer()
    {
        var container = _image.Container;
        var surface = _image.InputSurface;
        if (!surface.IsLoaded || !surface.IsVisible || !surface.IsMouseOver) return null;
        var point = Mouse.GetPosition(container);
        return point.X >= 0 && point.Y >= 0 && point.X < container.ActualWidth && point.Y < container.ActualHeight
            ? point : null;
    }

    public void Dispose() => Disable();
}
