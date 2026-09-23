using Fizzy.ImageViewer.Measurements;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

internal sealed class ShapeEditSession(UIElement shape, MeasurementItem? item, IShapeEditor editor) : IDisposable
{
    private Action<Point>? _drag;
    internal IReadOnlyList<Point> Points => item?.Geometry.ControlPoints ?? editor.GetControlPoints(shape);
    internal void BeginDrag(int index)
    {
        if (item != null)
        {
            var original = item.Geometry;
            _drag = point => item.UpdateGeometry(original.MoveControlPoint(index, point));
        }
        else _drag = editor.CreateDrag(shape, index);
    }
    internal void UpdateDrag(Point point) => _drag?.Invoke(point);
    internal void EndDrag() => _drag = null;
    public void Dispose() => EndDrag();
}
