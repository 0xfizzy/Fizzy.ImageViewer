using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class PointTool(IMeasurementContext context) : IMeasurementToolHandler
{
    public string Id => MeasurementToolIds.Point;
    public string DisplayName => "Point";
    public bool OnClick(Point point)
    {
        new MeasurementItem(context, MeasurementGeometry.Point(point), Shapes.CreatePoint(point, context.Style), Shapes.CreateLabel(point, "", 10, -20, context.Style)).Complete();
        return true;
    }
    public void OnMouseMove(Point point) { }
    public void Cancel() { }
}
