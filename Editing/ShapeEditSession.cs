using Fizzy.ImageViewer.Measurements;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

internal sealed class ShapeEditSession(IShapeEditTarget target) : IDisposable
{
    internal IReadOnlyList<Point> Points => target.Points;
    internal void BeginDrag(int index) => target.BeginDrag(index);
    internal void UpdateDrag(Point point) => target.Update(point);
    internal void EndDrag() => target.EndDrag();
    public void Dispose() => target.Dispose();
}
