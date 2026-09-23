using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Editing;

/// <summary>
/// Editor for Point shapes (Path with EllipseGeometry).
/// Control points: [0] = center position
/// </summary>
internal class PointEditor : IShapeEditor
{
    public IReadOnlyList<Point> GetControlPoints(UIElement shape)
    {
        if (shape is not FrameworkElement fe || fe.Tag is not OverlayShapeData data)
            return Array.Empty<Point>();

        return new[] { data.AnchorPoint };
    }

    public void UpdateControlPoint(UIElement shape, int pointIndex, Point newPosition)
    {
        if (shape is not FrameworkElement fe || fe.Tag is not OverlayShapeData data)
            return;

        if (pointIndex == 0)
        {
            data.AnchorPoint = newPosition;
            Canvas.SetLeft(fe, newPosition.X);
            Canvas.SetTop(fe, newPosition.Y);
        }
    }

}
