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
public class RectangleEditor : IShapeEditor
{
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
            case 0: // top-left
                Canvas.SetLeft(rect, newPosition.X);
                Canvas.SetTop(rect, newPosition.Y);
                rect.Width = right - newPosition.X;
                rect.Height = bottom - newPosition.Y;
                break;
            case 1: // top-right
                Canvas.SetTop(rect, newPosition.Y);
                rect.Width = newPosition.X - left;
                rect.Height = bottom - newPosition.Y;
                break;
            case 2: // bottom-right
                rect.Width = newPosition.X - left;
                rect.Height = newPosition.Y - top;
                break;
            case 3: // bottom-left
                Canvas.SetLeft(rect, newPosition.X);
                rect.Width = right - newPosition.X;
                rect.Height = newPosition.Y - top;
                break;
        }

        // Ensure positive dimensions
        if (rect.Width < 0)
        {
            Canvas.SetLeft(rect, Canvas.GetLeft(rect) + rect.Width);
            rect.Width = -rect.Width;
        }
        if (rect.Height < 0)
        {
            Canvas.SetTop(rect, Canvas.GetTop(rect) + rect.Height);
            rect.Height = -rect.Height;
        }

        // Update anchor point in tag data
        if (rect.Tag is OverlayTagData data)
        {
            data.AnchorPoint = new Point(Canvas.GetLeft(rect), Canvas.GetTop(rect));
        }
    }

    public string GetMeasurementText(UIElement shape)
    {
        if (shape is not Rectangle rect)
            return string.Empty;

        return $"{rect.Width:F1} x {rect.Height:F1} px";
    }

    public void UpdateLinkedShapes(UIElement shape)
    {
        if (shape is not FrameworkElement fe || fe.Tag is not OverlayTagData data)
            return;

        if (data.LinkedShapes == null) return;

        foreach (var linked in data.LinkedShapes)
        {
            if (linked is TextBlock label)
            {
                if (label.Tag is not OverlayTagData { PreserveMeasurementText: true }) label.Text = GetMeasurementText(shape);
            }
        }
    }
}
