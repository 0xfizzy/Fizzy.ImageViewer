using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.MeasureMethods;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shapes;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class LineStrengthTests
{
    private static Window[] Windows() => PresentationSource.CurrentSources.OfType<HwndSource>()
        .Select(source => source.RootVisual).OfType<Window>().ToArray();

    private static (Line Line, Window Window) Draw(Viewer viewer, LineStrengthMeasure method, OverlayLayer overlay)
    {
        var before = Windows();
        Assert.False(method.OnClick(new(0, 0), viewer.MeasurementContext));
        Assert.True(method.OnClick(new(1, 0), viewer.MeasurementContext));
        return (overlay.Canvas.Children.OfType<Line>().Last(), Assert.Single(Windows().Except(before)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosingEitherSideRemovesOnlyItsPairOnce(bool closeWindow)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Layers.Measurements.Root.Children.OfType<OverlayLayer>().Single();
            var method = new LineStrengthMeasure();
            var first = Draw(viewer, method, overlay);
            var second = Draw(viewer, method, overlay);
            int removed = 0, closed = 0;
            overlay.ShapeRemoved += shape => { if (ReferenceEquals(shape, first.Line)) removed++; };
            first.Window.Closed += (_, _) => closed++;
            if (closeWindow) first.Window.Close();
            else { overlay.Select(first.Line); overlay.EnterEditMode(); overlay.DeleteSelected(); }
            viewer.MeasurementContext.RemoveShape(first.Line);
            Assert.Equal(1, removed);
            Assert.Equal(1, closed);
            Assert.False(first.Window.IsVisible);
            Assert.True(second.Window.IsVisible);
            Assert.Equal(2, overlay.Canvas.Children.Count);
            Assert.Same(second.Line, Assert.Single(overlay.Canvas.Children.OfType<Line>()));
            viewer.ClearShapes();
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
            Assert.False(second.Window.IsVisible);
        });
    }

    [Fact]
    public async Task ContextDisposalClosesAllWindowsAndRemovesShapes()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Layers.Measurements.Root.Children.OfType<OverlayLayer>().Single();
            var method = new LineStrengthMeasure();
            var first = Draw(viewer, method, overlay);
            var second = Draw(viewer, method, overlay);
            viewer.MeasurementContext.Dispose();
            viewer.MeasurementContext.Dispose();
            Assert.False(first.Window.IsVisible);
            Assert.False(second.Window.IsVisible);
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Fact]
    public async Task ViewerShutdownClosesWindowsAndReleasesEveryVisualOnce()
    {
        var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        int closed = 0, removed = 0;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Layers.Measurements.Root.Children.OfType<OverlayLayer>().Single();
            var method = new LineStrengthMeasure();
            var first = Draw(viewer, method, overlay); var second = Draw(viewer, method, overlay);
            first.Window.Closed += (_, _) => closed++;
            second.Window.Closed += (_, _) => closed++;
            overlay.ShapeRemoved += _ => removed++;
        });
        await viewer.DisposeAsync(); await viewer.DisposeAsync();
        Assert.Equal(2, closed); Assert.Equal(4, removed);
    }
}
