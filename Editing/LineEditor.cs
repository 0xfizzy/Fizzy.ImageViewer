using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Editing;

/// <summary>
/// Editor for Line shapes.
/// Control points: [0] = start point (X1, Y1), [1] = end point (X2, Y2)
/// </summary>
internal class LineEditor : IShapeEditor
{
    public IReadOnlyList<Point> GetControlPoints(UIElement shape)
    {
        if (shape is not Line line)
            return Array.Empty<Point>();

        return new[] { new Point(line.X1, line.Y1), new Point(line.X2, line.Y2) };
    }

    public Action<Point> CreateDrag(UIElement shape, int pointIndex) => newPosition =>
    {
        if (shape is not Line line) return;

        switch (pointIndex)
        {
            case 0:
                line.X1 = newPosition.X;
                line.Y1 = newPosition.Y;
                break;
            case 1:
                line.X2 = newPosition.X;
                line.Y2 = newPosition.Y;
                break;
        }
    };

}
