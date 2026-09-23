using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Geometry;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Editing;

/// <summary>
/// Editor for Rectangle shapes.
/// Control points: 4 corners (top-left, top-right, bottom-right, bottom-left)
/// </summary>
internal class RectangleEditor : IShapeEditor
{
    public Action<Point> CreateDrag(UIElement shape, int index)
    {
        var points = GetControlPoints(shape);
        var rectangle = (Rectangle)shape;
        var opposite = points[(index + 2) % 4];
        return point =>
        {
            var (start, end) = GeometryOperations.NormalizeRectangle(opposite, point);
            rectangle.Width = end.X - start.X; rectangle.Height = end.Y - start.Y;
            Canvas.SetLeft(rectangle, start.X); Canvas.SetTop(rectangle, start.Y);
            if (OverlayShapeData.Get(rectangle) is { } data) data.AnchorPoint = start;
        };
    }
    public IReadOnlyList<Point> GetControlPoints(UIElement shape)
    {
        if (shape is not Rectangle rect)
            return Array.Empty<Point>();

        double left = Canvas.GetLeft(rect);
        double top = Canvas.GetTop(rect);
        double right = left + rect.Width;
        double bottom = top + rect.Height;

        return new[]
        {
            new Point(left, top),       // 0: top-left
            new Point(right, top),      // 1: top-right
            new Point(right, bottom),   // 2: bottom-right
            new Point(left, bottom)     // 3: bottom-left
        };
    }

}
