using Fizzy.ImageViewer.Interfaces;
using System.Windows;

namespace Fizzy.ImageViewer.MeasureMethods;

public class RectMeasure : IMeasureMethod
{
    public string Id => "ROI";
    public string DisplayName => "ROI";
    private Point _start;
    private RegionMeasurement? _item;
    public bool OnClick(Point point, IMeasureToolContext context)
    {
        if (_item == null || _item.IsDisposed)
        { _start = point; _item = new((MeasureContext)context, point); return false; }
        OnMouseMove(point, context);
        var item = _item; _item = null;
        try { item.Complete(); } catch { item.Dispose(); throw; }
        return true;
    }
    public void OnMouseMove(Point point, IMeasureToolContext context)
    {
        if (_item is { IsDisposed: false }) _item.UpdateGeometry(MeasurementGeometry.Rectangle(_start, point));
    }
    public void Cancel(IMeasureToolContext context) { var item = _item; _item = null; item?.Dispose(); }
}


