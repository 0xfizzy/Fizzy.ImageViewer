using Fizzy.ImageViewer.Drawing;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

internal sealed class VisualShapeEditTarget(UIElement shape, IShapeEditor editor) : IShapeEditTarget
{
    private Action<Point>? _drag;
    public IReadOnlyList<Point> Points => editor.GetControlPoints(shape);
    public void BeginDrag(int index) => _drag = editor.CreateDrag(shape, index);
    public void Update(Point point) => _drag?.Invoke(point);
    public void EndDrag() => _drag = null;
    public void Dispose() => EndDrag();
}
