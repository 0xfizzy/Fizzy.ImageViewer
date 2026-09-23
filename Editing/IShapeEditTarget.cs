using System.Windows;

namespace Fizzy.ImageViewer.Editing;

internal interface IShapeEditTarget : IDisposable
{
    IReadOnlyList<Point> Points { get; }
    void BeginDrag(int index);
    void Update(Point point);
    void EndDrag();
}
