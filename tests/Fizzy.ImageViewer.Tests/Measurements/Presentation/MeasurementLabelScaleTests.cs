using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementLabelScaleTests
{
    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public async Task EntireLabelAndOffsetKeepTheirScreenDimensions(double scale)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Host.Window.MeasurementOverlay;
            var anchor = new Point(30, 40);
            var label = MeasurementVisualFactory.CreateLabel(anchor, "Label", offsetX: 10, offsetY: -8);
            overlay.AddVisual(label);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            label.Arrange(new Rect(label.DesiredSize));
            var normal = label.RenderSize;
            overlay.UpdateScale(scale);
            var inverse = Assert.IsType<ScaleTransform>(label.RenderTransform);
            var net = inverse.Value;
            net.Scale(scale, scale);
            var screenBounds = new MatrixTransform(net).TransformBounds(new Rect(label.RenderSize));
            Assert.Equal(normal.Width, screenBounds.Width, 8);
            Assert.Equal(normal.Height, screenBounds.Height, 8);
            Assert.Equal(14, label.FontSize);
            Assert.Equal(new Thickness(3), label.Padding);
            Assert.Equal(14, label.FontSize * inverse.ScaleY * scale, 8);
            Assert.Equal(3, label.Padding.Left * inverse.ScaleX * scale, 8);
            Assert.Equal(10, (Canvas.GetLeft(label) - anchor.X) * scale, 8);
            Assert.Equal(-8, (Canvas.GetTop(label) - anchor.Y) * scale, 8);
            var transform = label.RenderTransform;
            overlay.UpdateAnchor(label, new(70, 80));
            Assert.Same(transform, label.RenderTransform);
            Assert.Equal(10, (Canvas.GetLeft(label) - 70) * scale, 8);
            Assert.Equal(-8, (Canvas.GetTop(label) - 80) * scale, 8);
            overlay.RemoveVisual(label);
        });
    }
}
