using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal class LineTool : IMeasurementTool
{
    public virtual string Id => MeasurementToolIds.Length;
    public virtual string DisplayName => "Length";
    protected virtual MeasurementOptions Options => new();
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => new Session(context, Options);

    private sealed class Session(IMeasurementToolContext context, MeasurementOptions options) : IMeasurementToolSession
    {
        private Point _start;
        private IMeasurement? _item;

        public bool OnClick(Point point)
        {
            if (_item == null || _item.IsDisposed)
            {
                _start = point;
                _item = context.CreateMeasurement(MeasurementGeometry.Line(point, point), options);
                return false;
            }
            OnMouseMove(point);
            _item.Complete();
            return true;
        }

        public void OnMouseMove(Point point)
        {
            if (_item is { IsDisposed: false })
                _item.UpdateGeometry(MeasurementGeometry.Line(_start, point));
        }

        public void Cancel() { }
    }
}
