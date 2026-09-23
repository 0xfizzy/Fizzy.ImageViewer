using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

internal sealed class CustomMeasurementTool(IMeasurementTool tool, IMeasurementToolContext context) : IMeasurementToolHandler
{
    public string Id => tool.Id;
    public string DisplayName => tool.DisplayName;
    public bool OnClick(Point point) => tool.OnClick(point, context);
    public void OnMouseMove(Point point) => tool.OnMouseMove(point, context);
    public void Cancel() => tool.Cancel(context);
}
