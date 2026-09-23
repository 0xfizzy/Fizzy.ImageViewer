using Fizzy.ImageViewer.Interfaces;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

internal sealed class CustomMeasureTool(IMeasureMethod method, IMeasureToolContext context) : IMeasureTool
{
    public string Id => method.Id;
    public string DisplayName => method.DisplayName;
    public bool OnClick(Point point) => method.OnClick(point, context);
    public void OnMouseMove(Point point) => method.OnMouseMove(point, context);
    public void Cancel() => method.Cancel(context);
}
