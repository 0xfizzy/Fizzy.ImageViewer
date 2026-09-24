using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class PointTool : IMeasurementTool
{
    public string Id => MeasurementToolIds.Point;
    public string DisplayName => "Point";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => new Session(context);

    private sealed class Session(IMeasurementToolContext context) : IMeasurementToolSession
    {
        public MeasurementClickResult OnClick(Point point)
        {
            context.CreateMeasurement(MeasurementGeometry.Point(point)).Complete();
            return MeasurementClickResult.Finish;
        }

        public void OnMouseMove(Point point) { }
        public void Cancel() { }
        public void Dispose() { }
    }
}
