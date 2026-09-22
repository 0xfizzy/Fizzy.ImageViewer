using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using System.Windows.Input;

namespace Fizzy.ImageViewer.PixelInfo;

public sealed class PixelInfoOverlay : IFrameMeasurement, IDisposable
{
    private readonly ImageLayer _image;
    private readonly HudLayer _hud;
    private readonly MeasureContext _context;
    private double _x,_y;
    private bool _inside;
    private long _generation;
    private readonly Guid _id = Guid.NewGuid();
    private MeasurementSubscription? _subscription;
    public bool IsEnabled { get; private set; }
    internal PixelInfoOverlay(ImageLayer image,HudLayer hud,MeasureContext context) { _image=image; _hud=hud; _context=context; }
    public void Enable() { if(IsEnabled)return; _subscription = _context.Register(this); IsEnabled=true; _generation++; _image.ImageMouseMove+=Move; _image.Container.MouseLeave+=Leave; }
    public void Disable() { if(!IsEnabled)return; IsEnabled=false; _inside=false; _generation++; _image.ImageMouseMove-=Move; _image.Container.MouseLeave-=Leave; _subscription?.Dispose(); _subscription = null; ClearResult(); }
    public void RequestUpdate() { }
    private void Move(double x,double y)
    {
        if (!_inside || Math.Floor(x) != Math.Floor(_x) || Math.Floor(y) != Math.Floor(_y))
        {
            _generation++;
            ClearResult();
        }
        _x=x; _y=y; _inside=true;
    }
    private void Leave(object sender,MouseEventArgs e) { _inside=false; _generation++; ClearResult(); }
    QueryRequest? IFrameMeasurement.Capture(FrameDescriptor d)
    {
        if(!IsEnabled||!_inside||!double.IsFinite(_x)||!double.IsFinite(_y)||_x<0||_y<0||_x>=d.Width||_y>=d.Height)return null;
        int x=(int)Math.Floor(_x),y=(int)Math.Floor(_y);
        return new PixelQueryRequest(new(_id, _generation), [new(x,y)], samples => {
            _hud.UpdatePixelInfo($"X: {x}, Y: {y} | {samples![0]}".AsSpan()); _hud.SetPixelInfoVisible(true);
        });
    }
    public void ClearResult()=>_hud.SetPixelInfoVisible(false);
    public void Dispose()=>Disable();
}
