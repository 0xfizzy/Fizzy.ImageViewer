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
