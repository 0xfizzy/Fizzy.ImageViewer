using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

internal interface IMeasureTool
{
    string Id { get; }
    string DisplayName { get; }
    bool OnClick(Point point);
    void OnMouseMove(Point point);
    void Cancel();
}
