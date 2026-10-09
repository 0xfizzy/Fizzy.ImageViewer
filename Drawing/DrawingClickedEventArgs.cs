using System.Windows;
using System.Windows.Input;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Identifies the whole drawing that was hit; no individual element index is reported.</summary>
/// <param name="drawing">Live handle of the drawing that was hit.</param>
/// <param name="imagePosition">Pointer position in source-image pixels.</param>
/// <param name="button">Mouse button that triggered the click.</param>
public sealed class DrawingClickedEventArgs(DrawingHandle drawing, Point imagePosition, MouseButton button) : EventArgs
{
    /// <summary>Live handle for the entire drawing that was hit.</summary>
    public DrawingHandle Drawing { get; } = drawing;
    /// <summary>Pointer location in source-image pixel coordinates.</summary>
    public Point ImagePosition { get; } = imagePosition;
    /// <summary>Mouse button that triggered the click.</summary>
    public MouseButton Button { get; } = button;
}
