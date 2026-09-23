using Fizzy.ImageViewer.Interfaces;
using System.Windows;

namespace Fizzy.ImageViewer.MeasureMethods;

public class PointMeasure : IMeasureMethod
{
    public string Id => "Point";
    public string DisplayName => "Point";
    public bool OnClick(Point point, IMeasureToolContext context)
    {
        new MeasurementItem((IMeasurementContext)context, MeasurementGeometry.Point(point), Shapes.CreatePoint(point), Shapes.CreateLabel(point, "", 10, -20)).Complete();
        return true;
    }
    public void OnMouseMove(Point point, IMeasureToolContext context) { }
    public void Cancel(IMeasureToolContext context) { }
}


