using System.Windows;
using System.Windows.Input;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Identifies the whole drawing that was hit; no individual element index is reported.</summary>
public sealed class DrawingClickedEventArgs(DrawingHandle drawing, Point imagePosition, MouseButton button) : EventArgs
{
    public DrawingHandle Drawing { get; } = drawing;
    public Point ImagePosition { get; } = imagePosition;
    public MouseButton Button { get; } = button;
}
