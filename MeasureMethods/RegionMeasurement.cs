using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.MeasureMethods;

internal sealed class RegionMeasurement : IFrameMeasurement
{
    private readonly Rectangle _rectangle;
    private readonly TextBlock _label;
    private readonly MeasureContext _context;
    private bool _disposed;
    public RegionMeasurement(Rectangle rectangle,MeasureContext context)
    {
        _rectangle=rectangle;_context=context;
        _label=Shapes.CreateLabel(new(Canvas.GetLeft(rectangle),Canvas.GetTop(rectangle)),"等待数据",5,0);
        if(_label.Tag is OverlayTagData labelTag)labelTag.PreserveMeasurementText=true;
        context.AddShape(_label);
        if(rectangle.Tag is OverlayTagData tag) { tag.LinkedShapes=[_label]; tag.OnRemoved=Dispose; tag.IsQueryRegion=true; }
        context.Register(this);
    }
    public QueryRequest? Capture(FrameDescriptor d)
    {
        if(_disposed)return null;
        double x=Canvas.GetLeft(_rectangle),y=Canvas.GetTop(_rectangle),w=_rectangle.Width,h=_rectangle.Height;
        var region=PixelRegion.Clip(x,y,w,h,d);if(region.IsEmpty)return null;
        return new((x,y,w,h,(_rectangle.Tag as OverlayTagData)?.GeometryVersion ?? 0),QueryKind.Region,null,region,(_,stats)=> {
            _context.UpdateAnchor(_label,new(x,y));
            var names=stats!.Channels.Length==1?new[]{"Gray"}:new[]{"R","G","B","A"};
            _label.Text=$"{region.Width}×{region.Height} px | "+string.Join(" | ",stats.Channels.Select((s,i)=>$"{names[i]}: n={s.Count} min={s.Minimum:G7} max={s.Maximum:G7} mean={s.Mean:G7}"));
        });
    }
    public void ClearResult() { if(!_disposed)_label.Text="等待数据"; }
    public void Dispose() { if(_disposed)return;_disposed=true;_context.Unregister(this);_context.RemoveShape(_label); }
}
