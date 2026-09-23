using Fizzy.ImageViewer.Measurements;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

internal sealed class MeasurementShapeEditTarget(MeasurementItem item) : IShapeEditTarget
{
    private Action<Point>? _drag;
    public IReadOnlyList<Point> Points => item.Geometry.ControlPoints;
    public void BeginDrag(int index)
    {
        var original = item.Geometry;
        _drag = point => item.UpdateGeometry(original.MoveControlPoint(index, point));
    }
    public void Update(Point point) => _drag?.Invoke(point);
    public void EndDrag() => _drag = null;
    public void Dispose() => EndDrag();
}
