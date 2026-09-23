using System.Windows;
namespace Fizzy.ImageViewer.Measurements.BuiltIn;
internal class LineTool : IMeasurementTool
{
    public virtual string Id => MeasurementToolIds.Length;
    public virtual string DisplayName => "Length";
    protected virtual MeasurementOptions Options => new();
    private Point _start;
    private IMeasurement? _item;
    public bool OnClick(Point point, IMeasurementToolContext context)
    {
        if (_item == null || _item.IsDisposed)
        { _start = point; _item = context.CreateMeasurement(MeasurementGeometry.Line(point, point), Options); return false; }
        OnMouseMove(point, context);
        var item = _item; _item = null;
        item.Complete(); return true;
    }
    public void OnMouseMove(Point point, IMeasurementToolContext context)
    { if (_item is { IsDisposed: false }) _item.UpdateGeometry(MeasurementGeometry.Line(_start, point)); }
    public void Cancel(IMeasurementToolContext context) { var item = _item; _item = null; item?.Dispose(); }
}
