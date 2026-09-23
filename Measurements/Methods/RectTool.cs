using System.Windows;

namespace Fizzy.ImageViewer.Measurements.Methods;

internal sealed class RectTool(IMeasurementContext context) : IMeasureTool
{
    public string Id => MeasureToolIds.ROI;
    public string DisplayName => "ROI";
    private Point _start;
    private RegionMeasurement? _item;
    public bool OnClick(Point point)
    {
        if (_item == null || _item.IsDisposed)
        { _start = point; _item = new(context, point); return false; }
        OnMouseMove(point);
        var item = _item; _item = null;
        try { item.Complete(); } catch { item.Dispose(); throw; }
        return true;
    }
    public void OnMouseMove(Point point)
    {
        if (_item is { IsDisposed: false }) _item.UpdateGeometry(MeasurementGeometry.Rectangle(_start, point));
    }
    public void Cancel() { var item = _item; _item = null; item?.Dispose(); }
}


