using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class TwoPointCreationSession(IMeasurementToolContext context,
    Func<Point, Point, MeasurementGeometry> geometry, MeasurementOptions options) : IMeasurementToolSession
{
    private Point _start;
    private IMeasurement? _item;

    public bool OnClick(Point point)
    {
        if (_item is null or { IsDisposed: true })
        {
            _start = point;
            _item = context.CreateMeasurement(geometry(point, point), options);
            return false;
        }
        OnMouseMove(point);
        _item.Complete();
        return true;
    }

    public void OnMouseMove(Point point)
    {
        if (_item is { IsDisposed: false })
            _item.UpdateGeometry(geometry(_start, point));
    }

    public void Cancel() { }
    public void Dispose() { }
}
