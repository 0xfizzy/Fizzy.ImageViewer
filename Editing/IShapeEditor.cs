using System.Collections.Generic;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

/// <summary>
/// Interface for shape-specific editing logic.
/// Each shape type (Line, Rectangle, Circle, etc.) has its own editor implementation.
/// </summary>
internal interface IShapeEditor
{
    /// <summary>
    /// Get control points in image coordinates.
    /// </summary>
    IReadOnlyList<Point> GetControlPoints(UIElement shape);

    /// <summary>Captures the drag-start geometry and returns the update operation.</summary>
    Action<Point> CreateDrag(UIElement shape, int pointIndex);
}
