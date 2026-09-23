using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Interfaces;
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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            using var scope = viewer.MeasurementContext.CreateScope();
            var rectangle = Shapes.CreateRectangle();
            var label = Shapes.CreateLabel(new(2, 3));
            label.FontSize = 24;
            var point = Shapes.CreatePoint(new(4, 5), new() { PointBrush = Brushes.Blue, SelectedBrush = Brushes.White });
            var tag = new object();
            rectangle.Tag = label.Tag = point.Tag = tag;
            scope.AddShape(rectangle); scope.AddShape(label); scope.AddShape(point);
            scope.Complete();
            var overlay = viewer.WindowForTests.MeasurementOverlay;
            Assert.Equal(1, rectangle.StrokeThickness);
            overlay.UpdateScale(2);
            Assert.Equal(0.5, rectangle.StrokeThickness);
            Assert.Equal(12, label.FontSize);
            scope.UpdateAnchor(point, new(8, 9));
            Assert.Equal(8, System.Windows.Controls.Canvas.GetLeft(point));
            viewer.Interaction.Select(point);
            Assert.Same(Brushes.White, point.Fill);
            Assert.Null(point.Stroke);
            viewer.Interaction.ClearSelection();
            Assert.Same(Brushes.Blue, point.Fill);
            point.Tag = "consumer data changed";
            viewer.Interaction.StartEditing(point);
            Assert.True(viewer.Interaction.Editor.BeginDrag(new(8, 9), 1));
            viewer.Interaction.Editor.UpdateDrag(new(10, 11));
            viewer.Interaction.StopEditing();
            overlay.UpdateScale(1);
            Assert.Equal(1, rectangle.StrokeThickness);
            Assert.Equal(24, label.FontSize);
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
            viewer.StartMeasure(MeasureToolIds.Length);
            viewer.Interaction.ImageDown(1, 1);
            viewer.Interaction.ImageDown(4, 4);
            return viewer.WindowForTests.MeasurementOverlay.Canvas.Children.OfType<Line>().Last();
        }
        await first.UiDispatcher.InvokeAsync(() =>
        {
            var line = CompleteLine(first);
            Assert.Equal(Colors.Blue, ((SolidColorBrush)line.Stroke).Color);
            first.MeasurementStyle = new() { NormalBrush = Brushes.Purple, SelectedBrush = Brushes.Orange };
            first.Interaction.Select(line);
            Assert.Same(Brushes.White, line.Stroke);
            first.Interaction.ClearSelection();
            Assert.Equal(Colors.Blue, ((SolidColorBrush)line.Stroke).Color);
            Assert.Same(Brushes.Purple, CompleteLine(first).Stroke);
        });
        await second.UiDispatcher.InvokeAsync(() => Assert.Same(Brushes.LimeGreen, CompleteLine(second).Stroke));
        await first.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => first.MeasurementStyle);
        Assert.Throws<ObjectDisposedException>(() => first.MeasurementStyle = new());
    }

    private sealed class StyledTool : IMeasureMethod
    {
        public string Id => "styled";
        public string DisplayName => "Styled";
        public bool OnClick(Point point, IMeasureToolContext context)
        {
            var scope = context.CreateScope();
            scope.AddShape(Shapes.CreateCircle(point, 2, context.Style));
            scope.Complete();
            return true;
        }
        public void OnMouseMove(Point point, IMeasureToolContext context) { }
        public void Cancel(IMeasureToolContext context) { }
    }

    [Fact]
    public async Task CustomToolsShareTheViewerStyleWithoutGlobalState()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        viewer.MeasurementStyle = new() { NormalBrush = Brushes.Purple };
        viewer.RegisterMeasureMethod(new StyledTool());
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.StartMeasure("styled");
            viewer.Interaction.ImageDown(3, 4);
            var shape = Assert.Single(viewer.WindowForTests.MeasurementOverlay.Canvas.Children.OfType<System.Windows.Shapes.Path>());
            Assert.Same(Brushes.Purple, shape.Stroke);
            viewer.ClearShapes();
        });
        Assert.Throws<ArgumentNullException>(() => viewer.MeasurementStyle = null!);
        Assert.Throws<ArgumentNullException>(() => viewer.MeasurementStyle = new() { NormalBrush = null! });
        Assert.Same(Brushes.Purple, viewer.MeasurementStyle.NormalBrush);
    }
}
