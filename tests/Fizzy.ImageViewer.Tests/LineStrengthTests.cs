using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Measurements.BuiltIn;
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
    [Fact]
    public void PixelReductionPreservesEndpointsExtremaAndOrder()
    {
        var points = new List<Point> { new(0, 5), new(.1, 3), new(.2, 9), new(.3, 1),
            new(.4, 4), new(.9, 6), new(1, 8), new(1.5, 2) };
        LineProfilePlotView.LineProfilePlotControl.ReduceToPixelColumns(points, 1);
        Assert.Equal(new Point[] { new(0, 5), new(.2, 9), new(.3, 1), new(.9, 6), new(1, 8), new(1.5, 2) }, points);
        var highDpi = new List<Point> { new(0, 0), new(.2, 2), new(.4, 1), new(.6, 3), new(.8, 0) };
        var expected = highDpi.ToArray();
        LineProfilePlotView.LineProfilePlotControl.ReduceToPixelColumns(highDpi, 5);
        Assert.Equal(expected, highDpi);
    }

    [Fact]
    public async Task RepeatedProfileUpdatesDoNotAllocateAfterWarmup()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var plot = new LineProfilePlotView.LineProfilePlotControl();
            var profile = Profile(new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255));
            for (int i = 0; i < 100; i++) { profile.Red[0] = i; plot.SetProfile(profile); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { profile.Red[0] = i; plot.SetProfile(profile); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        });
    }

    [Fact]
    public async Task RenderCachesGeometryAndKeepsNonFiniteGaps()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var plot = new LineProfilePlotView.LineProfilePlotControl();
            var profile = Profile(new(FramePixelFormat.Gray8, 1, 0, 0, 0, 255),
                new(FramePixelFormat.Gray8, 2, 0, 0, 0, 255),
                new(FramePixelFormat.Gray8, double.NaN, 0, 0, 0, 255),
                new(FramePixelFormat.Gray8, 3, 0, 0, 0, 255),
                new(FramePixelFormat.Gray8, 4, 0, 0, 0, 255));
            plot.Measure(new(600, 400));
            plot.Arrange(new(0, 0, 600, 400));
            var render = (Action<System.Windows.Media.DrawingContext>)plot.GetType()
                .GetMethod("OnRender", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .CreateDelegate(typeof(Action<System.Windows.Media.DrawingContext>), plot);
            var visual = new System.Windows.Media.DrawingVisual();
            System.Windows.Media.Geometry Curve()
            {
                using (var dc = visual.RenderOpen()) render(dc);
                return visual.Drawing.Children.OfType<System.Windows.Media.GeometryDrawing>().Last().Geometry;
            }
            plot.SetProfile(profile);
            var first = Curve();
            Assert.True(first.IsFrozen);
            Assert.Equal(2, System.Windows.Media.PathGeometry.CreateFromGeometry(first).Figures.Count);
            plot.SetProfile(profile);
            Assert.Same(first, Curve());
            profile.Red[0] = 2;
            plot.SetProfile(profile);
            var changed = Curve();
            Assert.NotSame(first, changed);
            plot.Arrange(new(0, 0, 800, 400));
            Assert.NotSame(changed, Curve());
            plot.Clear();
            using (var dc = visual.RenderOpen()) render(dc);
            Assert.Single(visual.Drawing.Children); // Background only, no stale curves.
        });
    }

    private sealed class FailingResourceMeasurement(IMeasurementContext context)
        : MeasurementItem(context, MeasurementGeometry.Point(new()), Shapes.CreatePoint(new()), Shapes.CreateLabel(new()))
    {
        protected override void OnDisposing() => throw new InvalidOperationException("resource cleanup failure");
    }

    [Fact]
    public async Task ResourceFailureStillDetachesMeasurementAndAllowsRepeatedDisposal()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.WindowForTests.MeasurementOverlay;
            var item = new FailingResourceMeasurement(viewer.MeasurementContext);
            item.Complete();
            int removed = 0;
            viewer.MeasurementRemoved += (_, _) => removed++;
            Assert.Throws<InvalidOperationException>(item.Dispose);
            Assert.True(item.IsDisposed);
            Assert.Null(viewer.MeasurementContext.Find(item.PrimaryVisual));
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
            Assert.Equal(1, removed);
            item.Dispose();
            Assert.Equal(1, removed);
        });
    }

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
                var plot = (LineProfilePlotView.LineProfilePlotControl)view.Window.Content;
                PixelSample rgb = new(FramePixelFormat.Bgr24, 0, 10, 20, 30, 255);
                view.Clear();
                Assert.Equal(0, plot.SampleCount);
                Assert.Equal(0, plot.ChannelCount);
                // Empty profiles may retain allocated source arrays.
                view.ShowProfile(Profile());
                Assert.Equal(0, plot.SampleCount);
                view.ShowProfile(Profile(rgb, rgb));
                view.ShowProfile(Profile(rgb, rgb, rgb, rgb));
                Assert.Equal(4, plot.SampleCount);
                Assert.Equal(3, plot.ChannelCount);
                var buffers = Enumerable.Range(0, 3).Select(plot.GetSamples).ToArray();

                view.ShowProfile(Profile(new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255)));
                Assert.Equal(1, plot.SampleCount);
                Assert.Equal(1, plot.ChannelCount);
                Assert.Equal(42, plot.GetSamples(0).Span[0]);

                view.ShowProfile(Profile(rgb, rgb));
                Assert.Equal(2, plot.SampleCount);
                Assert.Equal(3, plot.ChannelCount);
                for (int i = 0; i < 3; i++)
                {
                    Assert.True(buffers[i].Slice(0, 2).Equals(plot.GetSamples(i)));
                    Assert.Equal((i + 1) * 10, plot.GetSamples(i).Span[1]);
                }
                view.Clear();
                Assert.Equal(0, plot.SampleCount);
                Assert.Equal(0, plot.ChannelCount);
                view.ShowProfile(Profile(rgb));
                Assert.Equal(3, plot.ChannelCount);
                view.ShowProfile(Profile());
                Assert.Equal(0, plot.SampleCount);
                Assert.Equal(0, plot.ChannelCount);
            }
            finally { view.Window.Close(); }
        });
    }

    [Fact]
    public async Task PlotCopiesOnlyActiveSamplesFromRetainedProfileCapacity()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var view = new LineProfilePlotView();
            try
            {
                var profile = Profile(Enumerable.Repeat(
                    new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255), 8).ToArray());
                profile.Prepare(new(8, 1, 8, FramePixelFormat.Gray8), 0, 0, 1, 0);
                profile.Apply([new(FramePixelFormat.Gray8, 10, 0, 0, 0, 255),
                    new(FramePixelFormat.Gray8, 20, 0, 0, 0, 255)]);
                Assert.Equal(8, profile.Red.Length);
                view.ShowProfile(profile);
                var plot = (LineProfilePlotView.LineProfilePlotControl)view.Window.Content;
                Assert.Equal(2, plot.SampleCount);
                Assert.Equal(new double[] { 10, 20 }, plot.GetSamples(0).ToArray());
                profile.Apply([]);
                view.ShowProfile(profile);
                Assert.Equal(0, plot.ChannelCount);
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
                var plot = (LineProfilePlotView.LineProfilePlotControl)view.Window.Content;
                for (int channel = 0; channel < 3; channel++)
                    Assert.True(double.IsNaN(plot.GetSamples(channel).Span[1]));
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
            var request = Assert.IsType<LineProfileQueryRequest>(((IFrameQueryClient)item).Capture(descriptor));
            request.Publish([new(FramePixelFormat.Gray8, 10, 0, 0, 0, 255), new(FramePixelFormat.Gray8, 20, 0, 0, 0, 255)]);
            var plot = (LineProfilePlotView.LineProfilePlotControl)pair.Window.Content;
            Assert.Equal(2, plot.SampleCount);
            Assert.Equal(1, plot.ChannelCount);

            item.UpdateGeometry(MeasurementGeometry.Line(new(0, 0), new(2, 0)));
            Assert.Equal(0, plot.SampleCount);
            Assert.Equal(0, plot.ChannelCount);
            var updated = Assert.IsType<LineProfileQueryRequest>(((IFrameQueryClient)item).Capture(descriptor));
            Assert.NotEqual(request.Identity, updated.Identity);
            updated.Publish(Enumerable.Repeat(new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255), updated.Coordinates.Length).ToArray());
            Assert.Equal(1, plot.ChannelCount);
            Assert.Equal(3, plot.SampleCount);
            Assert.Equal(42, plot.GetSamples(0).Span[2]);
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
