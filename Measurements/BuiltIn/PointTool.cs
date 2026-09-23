using System.Windows;
namespace Fizzy.ImageViewer.Measurements.BuiltIn;
internal sealed class PointTool : IMeasurementTool
{
    public string Id => MeasurementToolIds.Point;
    public string DisplayName => "Point";
    public bool OnClick(Point point, IMeasurementToolContext context)
    { context.CreateMeasurement(MeasurementGeometry.Point(point)).Complete(); return true; }
    public void OnMouseMove(Point point, IMeasurementToolContext context) { }
    public void Cancel(IMeasurementToolContext context) { }
}
