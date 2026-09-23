using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

internal interface IMeasurementToolHandler
{
    string Id { get; }
    string DisplayName { get; }
    bool OnClick(Point point);
    void OnMouseMove(Point point);
    void Cancel();
}
