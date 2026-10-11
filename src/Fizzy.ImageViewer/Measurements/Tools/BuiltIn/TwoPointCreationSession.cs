using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class TwoPointCreationSession(IMeasurementToolContext context,
    Func<Point, Point, MeasurementGeometry> geometry, MeasurementOptions options) : IMeasurementToolSession
{
    private Point _start;
    private IMeasurement? _item;

    public MeasurementClickResult OnClick(Point point)
    {
        if (_item is null or { IsDisposed: true })
        {
            _start = point;
            _item = context.CreateMeasurement(geometry(point, point), options);
            return MeasurementClickResult.Continue;
        }
        OnMouseMove(point);
        _item.Complete();
        return MeasurementClickResult.Finish;
    }

    public void OnMouseMove(Point point)
    {
        if (_item is { IsDisposed: false })
            _item.UpdateGeometry(geometry(_start, point));
    }

    public void Cancel() { }
    public void Dispose() { }
}
