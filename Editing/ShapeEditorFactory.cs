using Fizzy.ImageViewer.Measurements;
using System.Windows;
namespace Fizzy.ImageViewer.Editing;
internal static class ShapeEditorFactory
{
    internal static IShapeEditTarget? Create(UIElement shape, MeasurementItem? item)
        => item is { IsDisposed: false } ? new MeasurementShapeEditTarget(item) : null;
}
