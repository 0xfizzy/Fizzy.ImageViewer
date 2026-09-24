using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Frames;
using System.Windows.Input;

namespace Fizzy.ImageViewer.Hud;

internal sealed class PixelInfoController : IDisposable
{
    private readonly ImageViewport _image;
    private readonly PixelQueryScheduler _scheduler;
    private readonly PixelInfoState _state;
    private QuerySubscription? _subscription;
    private bool _enabled;
    public bool IsEnabled
    {
        get => _enabled;
        set { if (value) Enable(); else Disable(); }
    }

    internal PixelInfoController(ImageViewport image, HudLayer hud, PixelQueryScheduler scheduler)
    {
        _image = image;
        _scheduler = scheduler;
        _state = new(text =>
        {
            if (text != null) hud.UpdatePixelInfo(text.AsSpan());
            hud.SetPixelInfoVisible(text != null);
        });
    }

    public void Enable()
    {
        if (IsEnabled) return;
        _subscription = _scheduler.Register(_state);
        _enabled = true;
        _state.Enable();
        _image.ImageMouseMove += Move;
        _image.Container.MouseLeave += Leave;
    }

    public void Disable()
    {
        if (!IsEnabled) return;
        _enabled = false;
        _state.Disable();
        _image.ImageMouseMove -= Move;
        _image.Container.MouseLeave -= Leave;
        _subscription?.Dispose();
        _subscription = null;
    }
    private void Move(double x, double y) => _state.Move(x, y);
    private void Leave(object sender, MouseEventArgs e) => _state.Leave();
    public void Dispose() => Disable();
}
