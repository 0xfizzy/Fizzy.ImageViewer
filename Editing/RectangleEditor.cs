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
    internal static Action<Point> CreateDrag(UIElement shape, int index, IReadOnlyList<Point> points)
    {
        var rectangle = (Rectangle)shape;
        var opposite = points[(index + 2) % 4];
        return point =>
        {
            var (start, end) = GeometryOperations.NormalizeRectangle(opposite, point);
            rectangle.Width = end.X - start.X; rectangle.Height = end.Y - start.Y;
            Canvas.SetLeft(rectangle, start.X); Canvas.SetTop(rectangle, start.Y);
            if (rectangle.Tag is OverlayTagData data) data.AnchorPoint = start;
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

    public void UpdateControlPoint(UIElement shape, int pointIndex, Point newPosition)
    {
        if (shape is not Rectangle rect) return;

        double left = Canvas.GetLeft(rect);
        double top = Canvas.GetTop(rect);
        double right = left + rect.Width;
        double bottom = top + rect.Height;

        switch (pointIndex)
        {
            case 0: left = newPosition.X; top = newPosition.Y; break;
            case 1: right = newPosition.X; top = newPosition.Y; break;
            case 2: right = newPosition.X; bottom = newPosition.Y; break;
            case 3: left = newPosition.X; bottom = newPosition.Y; break;
            default: return;
        }

        // Normalize before writing any WPF property. This keeps the geometry
        // valid even when a handle crosses its opposite corner.
        var x = Math.Min(left, right);
        var y = Math.Min(top, bottom);
        var width = Math.Abs(right - left);
        var height = Math.Abs(bottom - top);
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = width;
        rect.Height = height;

        // Update anchor point in tag data
        if (rect.Tag is OverlayTagData data)
        {
            data.AnchorPoint = new Point(Canvas.GetLeft(rect), Canvas.GetTop(rect));
        }
    }

}
