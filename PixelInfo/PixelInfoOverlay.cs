using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using System.Windows.Input;

namespace Fizzy.ImageViewer.PixelInfo;

public sealed class PixelInfoOverlay : IFrameMeasurement, IDisposable
{
    private readonly ImageLayer _image;
    private readonly MeasureContext _context;
    private readonly PixelInfoState _state;
    private MeasurementSubscription? _subscription;
    public bool IsEnabled { get; private set; }

    internal PixelInfoOverlay(ImageLayer image, HudLayer hud, MeasureContext context)
    {
        _image = image;
        _context = context;
        _state = new(text =>
        {
            if (text != null) hud.UpdatePixelInfo(text.AsSpan());
            hud.SetPixelInfoVisible(text != null);
        });
    }

    public void Enable()
    {
        if (IsEnabled) return;
        _subscription = _context.Register(this);
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
    MeasurementPolicy IFrameMeasurement.Policy => _state.Policy;
    QueryRequest? IFrameMeasurement.Capture(FrameDescriptor descriptor) => _state.Capture(descriptor);
    void IFrameMeasurement.InvalidateResult(ResultInvalidation reason) => _state.InvalidateResult(reason);
    public void ClearResult() => _state.ClearResult();
    public void Dispose() => Disable();
}
