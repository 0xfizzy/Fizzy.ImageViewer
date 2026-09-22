using Fizzy.ImageViewer.Interfaces;
using System.Windows;

namespace Fizzy.ImageViewer.MeasureMethods;

public class PointMeasure : IMeasureMethod
{
    public string Id => "Point";
    public string DisplayName => "Point";
    public bool OnClick(Point point, MeasureContext context)
    {
        new MeasurementItem(context, MeasurementGeometry.Point(point), Shapes.CreatePoint(point), Shapes.CreateLabel(point, "", 10, -20)).Complete();
        return true;
    }
    public void OnMouseMove(Point point, MeasureContext context) { }
    public void Cancel(MeasureContext context) { }
}
