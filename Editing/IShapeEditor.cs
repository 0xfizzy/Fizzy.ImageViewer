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

    /// <summary>
    /// Update shape geometry when a control point moves.
    /// </summary>
    /// <param name="shape">The shape being edited</param>
    /// <param name="pointIndex">Index of the control point being moved</param>
    /// <param name="newPosition">New position in image coordinates</param>
    void UpdateControlPoint(UIElement shape, int pointIndex, Point newPosition);

}
