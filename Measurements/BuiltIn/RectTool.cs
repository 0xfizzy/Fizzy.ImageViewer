using System.Windows;
namespace Fizzy.ImageViewer.Measurements.BuiltIn;
internal sealed class RectTool : IMeasurementTool
{
    public string Id => MeasurementToolIds.ROI;
    public string DisplayName => "ROI";
    private Point _start;
    private IMeasurement? _item;
    public bool OnClick(Point point, IMeasurementToolContext context)
    {
        if (_item == null || _item.IsDisposed)
        { _start = point; _item = context.CreateMeasurement(MeasurementGeometry.Rectangle(point, point), new() { Query = MeasurementQuery.RegionStatistics }); return false; }
        OnMouseMove(point, context);
        var item = _item; _item = null;
        item.Complete(); return true;
    }
    public void OnMouseMove(Point point, IMeasurementToolContext context)
    { if (_item is { IsDisposed: false }) _item.UpdateGeometry(MeasurementGeometry.Rectangle(_start, point)); }
    public void Cancel(IMeasurementToolContext context) { var item = _item; _item = null; item?.Dispose(); }
}
