using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using System.Windows.Input;

namespace Fizzy.ImageViewer.PixelInfo;

internal sealed class PixelInfoOverlay : IDisposable
{
    private readonly ImageLayer _image;
    private readonly PixelQueryScheduler _scheduler;
    private readonly PixelInfoState _state;
    private QuerySubscription? _subscription;
    public bool IsEnabled { get; private set; }

    internal PixelInfoOverlay(ImageLayer image, HudLayer hud, PixelQueryScheduler scheduler)
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
        IsEnabled = true;
        _state.Enable();
        _image.ImageMouseMove += Move;
        _image.Container.MouseLeave += Leave;
    }

    public void Disable()
    {
        if (!IsEnabled) return;
        IsEnabled = false;
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
