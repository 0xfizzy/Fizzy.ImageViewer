using Fizzy.ImageViewer.Measurements;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ControlVisualBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OverlaysRenderAndHitOnlyInsideTheOwnedViewport(bool measurement) => ControlTestDispatcher.Run(async () =>
    {
        await using var control = new ImageViewerControl { Width = 100, Height = 100, HorizontalAlignment = HorizontalAlignment.Left };
        await using var neighbor = new ImageViewerControl { Width = 100, Height = 100, HorizontalAlignment = HorizontalAlignment.Right };
        var host = new Grid { Width = 300, Height = 100, Background = Brushes.White };
        // Put the overflowing control last so a sibling cannot merely paint over the defect.
        host.Children.Add(neighbor);
        host.Children.Add(control);
        control.IsPixelInfoEnabled = neighbor.IsPixelInfoEnabled = false;
        if (measurement)
        {
            control.MeasurementStyle = new() { NormalBrush = Brushes.Red };
            control.ActivateMeasurementTool(MeasurementToolIds.Length);
            control.Runtime.Interaction.ImageDown(50, 50);
            control.Runtime.Interaction.ImageDown(280, 50);
        }
        else
        {
            control.Markers.IsHitTestVisible = true;
            control.DrawLine(new(50, 50), new(280, 50), Brushes.Red, 8);
        }

        Layout(host);
        Verify(host, control, neighbor, measurement, 75, 150, 50);

        // The clip follows layout, while geometry continues to share zoom and pan.
        control.Width = 120;
        control.ImageViewport.Scaler.ScaleX = control.ImageViewport.Scaler.ScaleY = 2;
        control.ImageViewport.Panner.X = -50;
        control.ImageViewport.Panner.Y = -25;
        Layout(host);
        Verify(host, control, neighbor, measurement, 100, 160, 75);
    });

    private static void Layout(FrameworkElement host)
    {
        host.Measure(new Size(300, 100));
        host.Arrange(new Rect(0, 0, 300, 100));
        host.UpdateLayout();
    }

    private static void Verify(Grid host, ImageViewerControl control, ImageViewerControl neighbor,
        bool measurement, int inside, int outside, int y)
    {
        var bitmap = new RenderTargetBitmap(300, 100, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(inside, y, 1, 1), pixel, 4, 0);
        Assert.True(pixel[2] > 100 && pixel[0] == 0 && pixel[1] == 0, "Visible overlay must still render red inside.");
        bitmap.CopyPixels(new Int32Rect(outside, y, 1, 1), pixel, 4, 0);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixel);
        bitmap.CopyPixels(new Int32Rect(250, y, 1, 1), pixel, 4, 0);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixel);

        var insideHit = VisualTreeHelper.HitTest(host, new Point(inside, y))!.VisualHit;
        Assert.True((measurement ? (Visual)control.MeasurementOverlay : control.Markers.Host).IsAncestorOf(insideHit));
        Assert.Same(host, VisualTreeHelper.HitTest(host, new Point(outside, y))!.VisualHit);
        var neighborHit = VisualTreeHelper.HitTest(host, new Point(250, y))!.VisualHit;
        Assert.True(neighbor.IsAncestorOf(neighborHit));
    }
}
