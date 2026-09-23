using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Measurements.Methods;
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

    private static (Line Line, Window Window) Draw(Viewer viewer, LineStrengthTool method, OverlayLayer overlay)
    {
        var before = Windows();
        Assert.False(method.OnClick(new(0, 0)));
        Assert.Empty(Windows().Except(before));
        Assert.True(method.OnClick(new(1, 0)));
        return (overlay.Canvas.Children.OfType<Line>().Last(), Assert.Single(Windows().Except(before)));
    }

    private static LineProfile Profile(params PixelSample[] samples)
    {
        var profile = new LineProfile();
        profile.Prepare(new(8, 1, 24, FramePixelFormat.Bgr24), 0, 0, Math.Max(0, samples.Length - 1), 0);
        profile.Apply(samples);
        return profile;
    }

    [Fact]
    public async Task PlotReusesCapacityWhenProfilesShrinkAndSwitchChannels()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var view = new LineProfilePlotView();
            try
            {
                var plot = (ScottPlot.WPF.WpfPlot)view.Window.Content;
                PixelSample rgb = new(FramePixelFormat.Bgr24, 0, 10, 20, 30, 255);
                view.Clear();
                Assert.Empty(plot.Plot.GetPlottables());
                view.ShowProfile(Profile(rgb, rgb));
                view.ShowProfile(Profile(rgb, rgb, rgb, rgb));
                var curves = plot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().ToArray();
                Assert.Equal(3, curves.Length);
                Assert.All(curves, curve => { Assert.True(curve.IsVisible); Assert.Equal(3, curve.Data.MaxRenderIndex); });

                view.ShowProfile(Profile(new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255)));
                Assert.True(curves[0].IsVisible);
                Assert.False(curves[1].IsVisible);
                Assert.False(curves[2].IsVisible);
                Assert.Equal(42, curves[0].Data.GetScatterPoints()[0].Y);
                Assert.All(curves, curve => Assert.Equal(0, curve.Data.MaxRenderIndex));

                view.ShowProfile(Profile(rgb, rgb));
                var reused = plot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().ToArray();
                for (int i = 0; i < curves.Length; i++)
                {
                    Assert.Same(curves[i], reused[i]);
                    Assert.True(curves[i].IsVisible);
                    Assert.Equal(1, curves[i].Data.MaxRenderIndex);
                    Assert.Equal((i + 1) * 10, curves[i].Data.GetScatterPoints()[1].Y);
                }
                view.Clear();
                Assert.All(curves, curve => Assert.False(curve.IsVisible));
                view.ShowProfile(Profile(rgb));
                Assert.All(curves, curve => Assert.True(curve.IsVisible));
                view.ShowProfile(Profile());
                Assert.All(curves, curve => Assert.False(curve.IsVisible));
            }
            finally { view.Window.Close(); }
        });
    }

    [Fact]
    public async Task PlotUsesGapsWithoutChangingRawNonFiniteSamples()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var view = new LineProfilePlotView();
            try
            {
                var profile = Profile(
                    new(FramePixelFormat.Bgr24, 0, 1, 2, 3, 255),
                    new(FramePixelFormat.Bgr24, 0, double.PositiveInfinity, double.NegativeInfinity, double.NaN, 255));
                view.ShowProfile(profile);
                var curves = ((ScottPlot.WPF.WpfPlot)view.Window.Content).Plot.GetPlottables<ScottPlot.Plottables.Scatter>();
                Assert.All(curves, curve => Assert.True(double.IsNaN(curve.Data.GetScatterPoints()[1].Y)));
                Assert.Equal(double.PositiveInfinity, profile.Red[1]);
                Assert.Equal(double.NegativeInfinity, profile.Green[1]);
                Assert.True(double.IsNaN(profile.Blue[1]));
            }
            finally { view.Window.Close(); }
        });
    }

    [Fact]
    public async Task EditingCompletedLineClearsPlotAndPublishesNewGeometry()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.WindowForTests.MeasurementOverlay;
            var pair = Draw(viewer, new LineStrengthTool(viewer.MeasurementContext), overlay);
            var item = viewer.MeasurementContext.Find(pair.Line)!;
            var descriptor = new FrameDescriptor(4, 1, 4, FramePixelFormat.Gray8);
            var request = Assert.IsType<LineProfileQueryRequest>(item.Capture(descriptor));
            request.Publish([new(FramePixelFormat.Gray8, 10, 0, 0, 0, 255), new(FramePixelFormat.Gray8, 20, 0, 0, 0, 255)]);
            var plot = (ScottPlot.WPF.WpfPlot)pair.Window.Content;
            Assert.True(plot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().First().IsVisible);

            item.UpdateGeometry(MeasurementGeometry.Line(new(0, 0), new(2, 0)));
            Assert.Null(item.Result);
            Assert.All(plot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>(), curve => Assert.False(curve.IsVisible));
            var updated = Assert.IsType<LineProfileQueryRequest>(item.Capture(descriptor));
            Assert.NotEqual(request.Identity, updated.Identity);
            updated.Publish(Enumerable.Repeat(new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255), updated.Coordinates.Length).ToArray());
            var red = plot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().First();
            Assert.True(red.IsVisible);
            Assert.Equal(2, red.Data.MaxRenderIndex);
            Assert.Equal(42, red.Data.GetScatterPoints()[2].Y);
            Assert.True(pair.Window.IsVisible);
            viewer.ClearShapes();
            Assert.False(pair.Window.IsVisible);
        });
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
            var method = new LineStrengthTool(viewer.MeasurementContext);
            var first = Draw(viewer, method, overlay);
            var second = Draw(viewer, method, overlay);
            int removed = 0, closed = 0;
            overlay.ShapeRemoved += shape => { if (ReferenceEquals(shape, first.Line)) removed++; };
            first.Window.Closed += (_, _) => closed++;
            if (closeWindow) first.Window.Close();
            else { viewer.Interaction.Select(first.Line); viewer.Interaction.StartEditing(viewer.Interaction.SelectedShape!); viewer.Interaction.DeleteSelected(); }
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
            var method = new LineStrengthTool(viewer.MeasurementContext);
            var first = Draw(viewer, method, overlay);
            var second = Draw(viewer, method, overlay);
            viewer.MeasurementContext.Shutdown();
            viewer.MeasurementContext.Shutdown();
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
            var method = new LineStrengthTool(viewer.MeasurementContext);
            var first = Draw(viewer, method, overlay); var second = Draw(viewer, method, overlay);
            first.Window.Closed += (_, _) => closed++;
            second.Window.Closed += (_, _) => closed++;
            overlay.ShapeRemoved += _ => removed++;
        });
        await viewer.DisposeAsync(); await viewer.DisposeAsync();
        Assert.Equal(2, closed); Assert.Equal(4, removed);
    }
}

