using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Editing;

/// <summary>
/// Editor for Crosshair shapes (Path with GeometryGroup).
/// Control points: [0] = center position
/// </summary>
public class CrosshairEditor : IShapeEditor
{
    public IReadOnlyList<Point> GetControlPoints(UIElement shape)
    {
        if (shape is not FrameworkElement fe || fe.Tag is not OverlayTagData data)
            return Array.Empty<Point>();

        return new[] { data.AnchorPoint };
    }

    public void UpdateControlPoint(UIElement shape, int pointIndex, Point newPosition)
    {
        if (shape is not FrameworkElement fe || fe.Tag is not OverlayTagData data)
            return;

        if (pointIndex == 0)
        {
            data.AnchorPoint = newPosition;
            Canvas.SetLeft(fe, newPosition.X);
            Canvas.SetTop(fe, newPosition.Y);
        }
    }

    public string GetMeasurementText(UIElement shape)
    {
        if (shape is not FrameworkElement fe || fe.Tag is not OverlayTagData data)
            return string.Empty;

        return $"({data.AnchorPoint.X:F1}, {data.AnchorPoint.Y:F1})";
    }

}
