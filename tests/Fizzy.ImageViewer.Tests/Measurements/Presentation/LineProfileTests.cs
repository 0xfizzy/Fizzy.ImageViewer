using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Viewport;
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
public class LineProfileTests
{
    [Fact]
    public async Task DataOnlyProfilePublishesRetainedResultsWithoutOpeningAPlot()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var before = Windows();
            var item = (MeasurementItem)new MeasurementCreationSession(viewer.Host.Measurements)
                .CreateMeasurement(MeasurementGeometry.Line(new(0, 0), new(1, 0)),
                    new() { Query = MeasurementQuery.LineProfile });
            item.Complete();
            var descriptor = new FrameDescriptor(2, 1, 2, FramePixelFormat.Gray8);
            var request = Assert.IsType<LineProfileQueryRequest>(item.QueryClient.Capture(descriptor));
            PixelSample[] samples = [new(FramePixelFormat.Gray8, 10, 0, 0, 0, 255),
                new(FramePixelFormat.Gray8, 20, 0, 0, 0, 255)];
            request.Publish(new(1, descriptor, null), samples);
            var retained = item.Result!;
            samples[0] = samples[0] with { Gray = 30 };
            request.Publish(new(2, descriptor, null), samples);
            Assert.Equal(10, retained.Samples[0].Gray);
            Assert.Equal(30, item.Result!.Samples[0].Gray);
            Assert.Empty(Windows().Except(before));
            item.Dispose();
            Assert.Equal(20, retained.Samples[1].Gray);
        });
    }

    [Fact]
    public async Task RegistryCallbackFailureStillReleasesPresentationBeforeRemovalNotification()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var store = viewer.Host.Measurements;
            var item = (MeasurementItem)new MeasurementCreationSession(store)
                .CreateMeasurement(MeasurementGeometry.Point(new()));
            item.Complete();
            var failure = new InvalidOperationException("registry observer failed");
            store.ItemRemoving += _ => throw failure;
            bool detachedAtNotification = false;
            store.ItemRemoved += removed => detachedAtNotification =
                !store.Contains(removed) && !store.Layer.Canvas.Children.Contains(removed.Presentation.PrimaryVisual)
                    && !store.Layer.Canvas.Children.Contains(removed.Presentation.Label);
            var error = Assert.Throws<AggregateException>(item.Dispose);
            Assert.Contains(failure, error.InnerExceptions);
            Assert.True(detachedAtNotification);
            item.Dispose();
        });
    }

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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var plot = new LineProfilePlotView.LineProfilePlotControl();
            var profile = Profile(new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255));
            for (int i = 0; i < 100; i++) { profile[0] = profile[0] with { Gray = i }; plot.SetProfile(profile); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { profile[0] = profile[0] with { Gray = i }; plot.SetProfile(profile); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        });
    }

    [Fact]
    public async Task RenderCachesGeometryAndKeepsNonFiniteGaps()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
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
            profile[0] = profile[0] with { Gray = 2 };
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

    [Fact]
    public async Task ResourceFailureStillDetachesMeasurementAndAllowsRepeatedDisposal()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Host.Window.MeasurementOverlay;
            var item = (MeasurementItem)new MeasurementCreationSession(viewer.Host.Measurements).CreateMeasurement(MeasurementGeometry.Point(new()));
            item.OnDispose(() => throw new InvalidOperationException("resource cleanup failure"));
            item.Complete();
            int removed = 0;
            viewer.MeasurementRemoved += (_, _) => removed++;
            Assert.Throws<AggregateException>(item.Dispose);
            Assert.True(item.IsDisposed);
            Assert.Null(viewer.Host.Measurements.Find(item.Presentation.PrimaryVisual));
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
            Assert.Equal(1, removed);
            item.Dispose();
            Assert.Equal(1, removed);
        });
    }

    private static Window[] Windows() => PresentationSource.CurrentSources.OfType<HwndSource>()
        .Select(source => source.RootVisual).OfType<Window>().ToArray();

    private static (Line Line, Window Window) Draw(Viewer viewer, LineProfileTool method, MeasurementOverlay overlay)
    {
        var before = Windows();
        var session = new MeasurementCreationSession(viewer.Host.Measurements);
        var activation = method.CreateSession(session);
        Assert.False(activation.OnClick(new(0, 0)));
        Assert.Empty(Windows().Except(before));
        Assert.True(activation.OnClick(new(1, 0)));
        session.End(); session.ClearPreviews();
        return (overlay.Canvas.Children.OfType<Line>().Last(), Assert.Single(Windows().Except(before)));
    }

    private static PixelSample[] Profile(params PixelSample[] samples) => samples;

    [Fact]
    public async Task PlotReusesCapacityWhenProfilesShrinkAndSwitchChannels()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var view = new LineProfilePlotView();
            try
            {
                var plot = (LineProfilePlotView.LineProfilePlotControl)view.Window.Content;
                PixelSample rgb = new(FramePixelFormat.Bgr24, 0, 10, 20, 30, 255);
                view.Clear();
                Assert.Equal(0, plot.SampleCount);
                Assert.Equal(0, plot.ChannelCount);
                // Empty results clear previously displayed samples.
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
    public async Task PlotCopiesOnlyTheRequestedSampleSlice()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var view = new LineProfilePlotView();
            try
            {
                var profile = Profile(Enumerable.Repeat(
                    new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255), 8).ToArray());
                profile[0] = new(FramePixelFormat.Gray8, 10, 0, 0, 0, 255);
                profile[1] = new(FramePixelFormat.Gray8, 20, 0, 0, 0, 255);
                view.ShowProfile(new ArraySegment<PixelSample>(profile, 0, 2));
                var plot = (LineProfilePlotView.LineProfilePlotControl)view.Window.Content;
                Assert.Equal(2, plot.SampleCount);
                Assert.Equal(new double[] { 10, 20 }, plot.GetSamples(0).ToArray());
                view.ShowProfile(Array.Empty<PixelSample>());
                Assert.Equal(0, plot.ChannelCount);
            }
            finally { view.Window.Close(); }
        });
    }

    [Fact]
    public async Task PlotUsesGapsWithoutChangingRawNonFiniteSamples()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
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
                Assert.Equal(double.PositiveInfinity, profile[1].R);
                Assert.Equal(double.NegativeInfinity, profile[1].G);
                Assert.True(double.IsNaN(profile[1].B));
            }
            finally { view.Window.Close(); }
        });
    }

    [Fact]
    public async Task EditingCompletedLineClearsPlotAndPublishesNewGeometry()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Host.Window.MeasurementOverlay;
            var pair = Draw(viewer, new LineProfileTool(), overlay);
            var item = viewer.Host.Measurements.Find(pair.Line)!;
            var descriptor = new FrameDescriptor(4, 1, 4, FramePixelFormat.Gray8);
            var request = Assert.IsType<LineProfileQueryRequest>(item.QueryClient.Capture(descriptor));
            request.Publish(new FrameInfo(1, descriptor, null), [new(FramePixelFormat.Gray8, 10, 0, 0, 0, 255), new(FramePixelFormat.Gray8, 20, 0, 0, 0, 255)]);
            var plot = (LineProfilePlotView.LineProfilePlotControl)pair.Window.Content;
            Assert.Equal(2, plot.SampleCount);
            Assert.Equal(1, plot.ChannelCount);

            item.UpdateGeometry(MeasurementGeometry.Line(new(0, 0), new(2, 0)));
            Assert.Equal(0, plot.SampleCount);
            Assert.Equal(0, plot.ChannelCount);
            var updated = Assert.IsType<LineProfileQueryRequest>(item.QueryClient.Capture(descriptor));
            Assert.NotEqual(request.Identity, updated.Identity);
            updated.Publish(new FrameInfo(2, descriptor, null), Enumerable.Repeat(new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255), updated.Coordinates.Length).ToArray());
            Assert.Equal(1, plot.ChannelCount);
            Assert.Equal(3, plot.SampleCount);
            Assert.Equal(42, plot.GetSamples(0).Span[2]);
            Assert.True(pair.Window.IsVisible);
            viewer.Layers.ClearContents();
            Assert.False(pair.Window.IsVisible);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosingEitherSideRemovesOnlyItsPairOnce(bool closeWindow)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Layers.Measurements.Root.Children.OfType<MeasurementOverlay>().Single();
            var method = new LineProfileTool();
            var first = Draw(viewer, method, overlay);
            var second = Draw(viewer, method, overlay);
            int removed = 0, closed = 0;
            overlay.VisualRemoved += shape => { if (ReferenceEquals(shape, first.Line)) removed++; };
            first.Window.Closed += (_, _) => closed++;
            if (closeWindow) first.Window.Close();
            else { viewer.Host.Interaction.Select(viewer.Host.Measurements.Find(first.Line)); viewer.Host.Interaction.StartEditing(viewer.Host.Interaction.SelectedMeasurement!); viewer.Host.Interaction.DeleteSelected(); }
            viewer.Host.Measurements.Find(first.Line)?.Dispose();
            Assert.Equal(1, removed);
            Assert.Equal(1, closed);
            Assert.False(first.Window.IsVisible);
            Assert.True(second.Window.IsVisible);
            Assert.Equal(2, overlay.Canvas.Children.Count);
            Assert.Same(second.Line, Assert.Single(overlay.Canvas.Children.OfType<Line>()));
            viewer.Layers.ClearContents();
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
            Assert.False(second.Window.IsVisible);
        });
    }

    [Fact]
    public async Task ContextDisposalClosesAllWindowsAndRemovesShapes()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Layers.Measurements.Root.Children.OfType<MeasurementOverlay>().Single();
            var method = new LineProfileTool();
            var first = Draw(viewer, method, overlay);
            var second = Draw(viewer, method, overlay);
            viewer.Host.Measurements.Shutdown();
            viewer.Host.Measurements.Shutdown();
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Layers.Measurements.Root.Children.OfType<MeasurementOverlay>().Single();
            var method = new LineProfileTool();
            var first = Draw(viewer, method, overlay); var second = Draw(viewer, method, overlay);
            first.Window.Closed += (_, _) => closed++;
            second.Window.Closed += (_, _) => closed++;
            overlay.VisualRemoved += _ => removed++;
        });
        await viewer.DisposeAsync(); await viewer.DisposeAsync();
        Assert.Equal(2, closed); Assert.Equal(4, removed);
    }
}
