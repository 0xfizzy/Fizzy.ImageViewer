using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class RectangleRoiTool : IMeasurementTool
{
    public string Id => MeasurementToolIds.ROI;
    public string DisplayName => "ROI";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => new Session(context);

    private sealed class Session(IMeasurementToolContext context) : IMeasurementToolSession
    {
        private Point _start;
        private IMeasurement? _item;

        public bool OnClick(Point point)
        {
            if (_item == null || _item.IsDisposed)
            {
                _start = point;
                _item = context.CreateMeasurement(MeasurementGeometry.Rectangle(point, point),
                    new() { Query = MeasurementQuery.RegionStatistics });
                return false;
            }
            OnMouseMove(point);
            _item.Complete();
            return true;
        }

        public void OnMouseMove(Point point)
        {
            if (_item is { IsDisposed: false })
                _item.UpdateGeometry(MeasurementGeometry.Rectangle(_start, point));
        }

        public void Cancel() { }
    }
}
