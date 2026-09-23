using Fizzy.ImageViewer.Interfaces;
using System.Windows;

namespace Fizzy.ImageViewer.MeasureMethods;

public class LineMeasure : IMeasureMethod
{
    public virtual string Id => "Length";
    public virtual string DisplayName => "Length";
    private Point _start;
    private MeasurementItem? _item;
    private protected virtual MeasurementItem CreateItem(IMeasureToolContext context, Point start) =>
        new((IMeasurementContext)context, MeasurementGeometry.Line(start, start), Shapes.CreateLine(), Shapes.CreateLabel(start, "", 5, 0));
    public bool OnClick(Point point, IMeasureToolContext context)
    {
        if (_item == null || _item.IsDisposed)
        { _start = point; _item = CreateItem(context, point); return false; }
        OnMouseMove(point, context);
        var item = _item; _item = null;
        try { item.Complete(); } catch { item.Dispose(); throw; }
        return true;
    }
    public void OnMouseMove(Point point, IMeasureToolContext context)
    {
        if (_item is { IsDisposed: false }) _item.UpdateGeometry(MeasurementGeometry.Line(_start, point));
    }
    public void Cancel(IMeasureToolContext context) { var item = _item; _item = null; item?.Dispose(); }
}

