using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Editing;

/// <summary>
/// Editor for Circle shapes (Path with EllipseGeometry).
/// Control points: [0] = center, [1] = point on circumference (for radius control)
/// </summary>
public class CircleEditor : IShapeEditor
{
    public IReadOnlyList<Point> GetControlPoints(UIElement shape)
    {
        if (shape is not Path path || path.Data is not EllipseGeometry ellipse)
            return Array.Empty<Point>();

        var center = ellipse.Center;
        var radius = ellipse.RadiusX; // Assume circle (RadiusX == RadiusY)

        // Get absolute center position
        double left = Canvas.GetLeft(path);
        double top = Canvas.GetTop(path);
        var absoluteCenter = new Point(left + center.X, top + center.Y);

        // Point on circumference (to the right of center)
        var circumferencePoint = new Point(absoluteCenter.X + radius, absoluteCenter.Y);

        return new[] { absoluteCenter, circumferencePoint };
    }

    public void UpdateControlPoint(UIElement shape, int pointIndex, Point newPosition)
    {
        if (shape is not Path path || path.Data is not EllipseGeometry ellipse)
            return;

        if (pointIndex == 0)
        {
            // Moving center
            Canvas.SetLeft(path, newPosition.X);
            Canvas.SetTop(path, newPosition.Y);

            // Update anchor point in tag data
            if (path.Tag is OverlayTagData data)
            {
                data.AnchorPoint = newPosition;
            }
        }
        else if (pointIndex == 1)
        {
            // Moving circumference point - update radius
            double left = Canvas.GetLeft(path);
            double top = Canvas.GetTop(path);
            var center = new Point(left, top);

            double newRadius = Math.Sqrt(Math.Pow(newPosition.X - center.X, 2) +
                                        Math.Pow(newPosition.Y - center.Y, 2));

            ellipse.RadiusX = newRadius;
            ellipse.RadiusY = newRadius;
        }
    }

    public string GetMeasurementText(UIElement shape)
    {
        if (shape is not Path path || path.Data is not EllipseGeometry ellipse)
            return string.Empty;

        return $"R = {ellipse.RadiusX:F1} px";
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
                label.Text = GetMeasurementText(shape);
            }
        }
    }
}
