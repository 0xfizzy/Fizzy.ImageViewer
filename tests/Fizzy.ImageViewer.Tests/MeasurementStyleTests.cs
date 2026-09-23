using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementStyleTests
{
    [Fact]
    public async Task VisualMetadataDoesNotOccupyTagAndZoomRetainsPerVisualAppearance()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var rectangleItem = viewer.Host.Measurements.CreateMeasurement(MeasurementGeometry.Rectangle(new(2, 3), new(4, 5)));
            using var pointItem = viewer.Host.Measurements.CreateMeasurement(MeasurementGeometry.Point(new(4, 5)),
                new() { Style = new() { PointBrush = Brushes.Blue, SelectedBrush = Brushes.White } });
            var rectangle = (System.Windows.Shapes.Rectangle)((MeasurementItem)rectangleItem).Presentation.PrimaryVisual;
            var label = ((MeasurementItem)rectangleItem).Presentation.Label;
            var point = (System.Windows.Shapes.Path)((MeasurementItem)pointItem).Presentation.PrimaryVisual;
            var tag = new object(); rectangle.Tag = label.Tag = point.Tag = tag;
            rectangleItem.Complete(); pointItem.Complete();
            var overlay = viewer.Host.Window.MeasurementOverlay;
            Assert.Equal(1, rectangle.StrokeThickness);
            overlay.UpdateScale(2);
            Assert.Equal(0.5, rectangle.StrokeThickness);
            Assert.Equal(7, label.FontSize);
            pointItem.UpdateGeometry(MeasurementGeometry.Point(new(8, 9)));
            Assert.Equal(8, System.Windows.Controls.Canvas.GetLeft(point));
            viewer.Host.Interaction.Select(viewer.Host.Measurements.Find(point));
            Assert.Same(Brushes.White, point.Fill);
            Assert.Null(point.Stroke);
            viewer.Host.Interaction.ClearSelection();
            Assert.Same(Brushes.Blue, point.Fill);
            point.Tag = "consumer data changed";
            viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(point));
            Assert.True(viewer.Host.Interaction.Editor.BeginDrag(new(8, 9), 1));
            viewer.Host.Interaction.Editor.UpdateDrag(new(10, 11));
            viewer.Host.Interaction.StopEditing();
            overlay.UpdateScale(1);
            Assert.Equal(1, rectangle.StrokeThickness);
            Assert.Equal(14, label.FontSize);
            Assert.Equal(10, System.Windows.Controls.Canvas.GetLeft(point));
            Assert.Same(tag, rectangle.Tag);
            Assert.Same(tag, label.Tag);
            Assert.Equal("consumer data changed", point.Tag);
        });
    }

    [Fact]
    public async Task StylesAreFrozenIsolatedAndRetainedByExistingShapes()
    {
        await using var first = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await using var second = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await Task.Run(() =>
        {
            var source = new SolidColorBrush(Colors.Blue);
            first.MeasurementStyle = new() { NormalBrush = source, SelectedBrush = Brushes.White };
            source.Color = Colors.Red;
            var snapshot = first.MeasurementStyle;
            Assert.True(snapshot.NormalBrush.IsFrozen);
            Assert.Equal(Colors.Blue, ((SolidColorBrush)snapshot.NormalBrush).Color);
            Assert.Throws<InvalidOperationException>(() => ((SolidColorBrush)snapshot.NormalBrush).Color = Colors.Black);
        });
        static Line CompleteLine(Viewer viewer)
        {
            viewer.StartMeasurement(MeasurementToolIds.Length);
            viewer.Host.Interaction.ImageDown(1, 1);
            viewer.Host.Interaction.ImageDown(4, 4);
            return viewer.Host.Window.MeasurementOverlay.Canvas.Children.OfType<Line>().Last();
        }
        await first.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var line = CompleteLine(first);
            Assert.Equal(Colors.Blue, ((SolidColorBrush)line.Stroke).Color);
            first.MeasurementStyle = new() { NormalBrush = Brushes.Purple, SelectedBrush = Brushes.Orange };
            first.Host.Interaction.Select(first.Host.Measurements.Find(line));
            Assert.Same(Brushes.White, line.Stroke);
            first.Host.Interaction.ClearSelection();
            Assert.Equal(Colors.Blue, ((SolidColorBrush)line.Stroke).Color);
            Assert.Same(Brushes.Purple, CompleteLine(first).Stroke);
        });
        await second.Host.Window.Dispatcher.InvokeAsync(() => Assert.Same(Brushes.LimeGreen, CompleteLine(second).Stroke));
        await first.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => first.MeasurementStyle);
        Assert.Throws<ObjectDisposedException>(() => first.MeasurementStyle = new());
    }

    private sealed class StyledTool : IMeasurementTool
    {
        public string Id => "styled";
        public string DisplayName => "Styled";
        public bool OnClick(Point point, IMeasurementToolContext context)
        {
            var scope = context.CreateMeasurement(MeasurementGeometry.Circle(point, 2));
            scope.Complete();
            return true;
        }
        public void OnMouseMove(Point point, IMeasurementToolContext context) { }
        public void Cancel(IMeasurementToolContext context) { }
    }

    [Fact]
    public async Task CustomToolsShareTheViewerStyleWithoutGlobalState()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        viewer.MeasurementStyle = new() { NormalBrush = Brushes.Purple };
        viewer.RegisterMeasurementTool(new StyledTool());
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.StartMeasurement("styled");
            viewer.Host.Interaction.ImageDown(3, 4);
            var shape = Assert.Single(viewer.Host.Window.MeasurementOverlay.Canvas.Children.OfType<System.Windows.Shapes.Path>());
            Assert.Same(Brushes.Purple, shape.Stroke);
            viewer.ClearShapes();
        });
        Assert.Throws<ArgumentNullException>(() => viewer.MeasurementStyle = null!);
        Assert.Throws<ArgumentNullException>(() => viewer.MeasurementStyle = new() { NormalBrush = null! });
        Assert.Same(Brushes.Purple, viewer.MeasurementStyle.NormalBrush);
    }
}
