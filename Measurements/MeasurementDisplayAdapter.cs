using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>One-way projection from canonical geometry to WPF presentation.</summary>
internal sealed class MeasurementDisplayAdapter(MeasurementContext context, UIElement shape, TextBlock label)
{
    public void Apply(MeasurementGeometry geometry)
    {
        if (geometry.Kind == ShapeType.Circle && shape is Path path && path.Data is System.Windows.Media.EllipseGeometry ellipse)
        { ellipse.RadiusX = ellipse.RadiusY = geometry.Radius; }
        switch (shape)
        {
            case Rectangle rectangle:
                rectangle.Width = geometry.Width;
                rectangle.Height = geometry.Height;
                context.UpdateAnchor(rectangle, geometry.Start);
                break;
            case Line line:
                line.X1 = geometry.Start.X; line.Y1 = geometry.Start.Y;
                line.X2 = geometry.End.X; line.Y2 = geometry.End.Y;
                break;
            default:
                context.UpdateAnchor(shape, geometry.Start);
                break;
        }
        context.UpdateAnchor(label, geometry.Kind == ShapeType.Line ? geometry.End : geometry.Start);
    }
}
