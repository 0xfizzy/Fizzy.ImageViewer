using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal class LineTool(IMeasurementContext context) : IMeasurementToolHandler
{
    public virtual string Id => MeasurementToolIds.Length;
    public virtual string DisplayName => "Length";
    private Point _start;
    private MeasurementItem? _item;
    private protected virtual MeasurementItem CreateItem(IMeasurementContext context, Point start) =>
        new(context, MeasurementGeometry.Line(start, start), Shapes.CreateLine(context.Style), Shapes.CreateLabel(start, "", 5, 0, context.Style));
    public bool OnClick(Point point)
    {
        if (_item == null || _item.IsDisposed)
        { _start = point; _item = CreateItem(context, point); return false; }
        OnMouseMove(point);
        var item = _item; _item = null;
        try { item.Complete(); } catch { item.Dispose(); throw; }
        return true;
    }
    public void OnMouseMove(Point point)
    {
        if (_item is { IsDisposed: false }) _item.UpdateGeometry(MeasurementGeometry.Line(_start, point));
    }
    public void Cancel() { var item = _item; _item = null; item?.Dispose(); }
}

