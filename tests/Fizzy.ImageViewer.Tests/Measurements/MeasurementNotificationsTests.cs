using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementNotificationsTests
{
    private sealed class RoiTool : IMeasurementTool
    {
        public string Id => "notification-roi";
        public string DisplayName => Id;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => new Session(context);

        private sealed class Session(IMeasurementToolContext context) : IMeasurementToolSession
        {
            private IMeasurement? _preview;
            public MeasurementClickResult OnClick(Point point)
            {
                if (_preview == null)
                {
                    _preview = context.CreateMeasurement(MeasurementGeometry.Rectangle(point, point),
                        new() { Query = MeasurementQueryOptions.RegionStatistics });
                    return MeasurementClickResult.Continue;
                }
                _preview.UpdateGeometry(MeasurementGeometry.Rectangle(Assert.IsType<RectangleMeasurementGeometry>(_preview.Geometry).Start, point));
                _preview.Complete();
                return MeasurementClickResult.Finish;
            }
            public void OnMouseMove(Point point) { }
            public void Cancel() { }
            public void Dispose() { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuiltInAndCustomResultsAreObservableThroughThePublicFacade(bool custom)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        IViewer api = viewer;
        var events = new List<MeasurementEventArgs>();
        var ready = new TaskCompletionSource<MeasurementEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        MeasurementEventArgs? completed = null;
        MeasurementEventArgs? removed = null;
        var callbackThreads = new List<int>();
        api.MeasurementCompleted += (_, e) => completed = e;
        api.MeasurementRemoved += (_, e) => removed = e;
        api.MeasurementChanged += (_, _) => throw new InvalidOperationException("subscriber failure");
        api.MeasurementChanged += (_, e) =>
        {
            callbackThreads.Add(Environment.CurrentManagedThreadId);
            events.Add(e);
            if (e.Result != null) ready.TrySetResult(e);
        };
        var tool = new RoiTool();
        api.RegisterMeasurementTool(tool);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            api.StartMeasurement(custom ? tool.Id : MeasurementToolIds.RectangleRoi);
            viewer.Host.Interaction.ImageDown(0, 0);
            viewer.Host.Interaction.ImageMove(2, 2);
            Assert.Empty(events); // Previews never enter the public result stream.
            viewer.Host.Interaction.ImageDown(2, 2);
        });
        Assert.NotNull(completed);
        Assert.Null(completed.Result);
        var submission = await api.SubmitFrameAsync(ImageFrame.Copy(new(4, 4, 4, FramePixelFormat.Gray8),
            Enumerable.Range(0, 16).Select(x => (byte)x).ToArray()));
        var published = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(completed.Snapshot.Id, published.Snapshot.Id);
        Assert.Equal(submission.FrameId, published.Result!.Frame.FrameId);
        Assert.Equal(published.Snapshot.GeometryVersion, published.Result.GeometryVersion);
        Assert.Equal(2.5, Assert.Single(Assert.IsType<MeasurementRegionResult>(published.Result).Channels).Mean);

        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var shape = viewer.Host.Window.MeasurementOverlay.Canvas.Children.OfType<System.Windows.Shapes.Rectangle>().Single();
            var item = viewer.Host.Measurements.Find(shape)!;
            viewer.Host.Interaction.StartEditing(item);
            Assert.True(viewer.Host.Interaction.Editor.BeginDrag(new(0, 0), 1));
            viewer.Host.Interaction.Editor.UpdateDrag(new(3, 3));
            var edited = events.Last();
            Assert.Equal(completed.Snapshot.Id, edited.Snapshot.Id);
            Assert.Equal(completed.Snapshot.GeometryVersion + 1, edited.Snapshot.GeometryVersion);
            Assert.Equal(MeasurementGeometry.Rectangle(new(2, 2), new(3, 3)), edited.Snapshot.Geometry);
            Assert.Null(edited.Result);
            Assert.All(callbackThreads, id => Assert.Equal(Environment.CurrentManagedThreadId, id));
        });
        // Both geometry and samples remain snapshots after editing and cross-thread removal.
        Assert.Equal(completed.Snapshot.GeometryVersion, published.Snapshot.GeometryVersion);
        Assert.Equal(2.5, Assert.IsType<MeasurementRegionResult>(published.Result).Channels[0].Mean);
        await Task.Run(published.Measurement.Dispose);
        Assert.NotNull(removed);
        Assert.Equal(completed.Snapshot.GeometryVersion + 1, removed.Snapshot.GeometryVersion);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            int count = events.Count;
            viewer.Host.Queries.Tick();
            Assert.Equal(count, events.Count);
        });
    }

    [Fact]
    public async Task ChangedCallbackCanRemoveTheItemWithoutLeavingQueryOrVisualResources()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        int changed = 0, removed = 0;
        viewer.MeasurementChanged += (_, e) => { changed++; e.Measurement.Dispose(); };
        viewer.MeasurementRemoved += (_, _) => removed++;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements).CreateMeasurement(MeasurementGeometry.Point(new(0, 0)));
            item.Complete();
            item.UpdateGeometry(MeasurementGeometry.Point(new(1, 1)));
            Assert.True(item.IsDisposed);
            Assert.False(viewer.Host.Measurements.Contains(item));
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children);
        });
        Assert.Equal(1, changed);
        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task ReentrantGeometryChangesHaveOneOrderAcrossViewerAndHandleSubscribers()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var first = new List<long>();
        var second = new List<long>();
        var handleFirst = new List<double>();
        var handleSecond = new List<double>();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements)
                .CreateMeasurement(MeasurementGeometry.Point(new()));
            item.Complete();
            viewer.MeasurementChanged += (_, e) =>
            {
                first.Add(e.Snapshot.GeometryVersion);
                if (e.Snapshot.GeometryVersion == 1)
                    e.Measurement.UpdateGeometry(MeasurementGeometry.Point(new(2, 0)));
            };
            viewer.MeasurementChanged += (_, e) => second.Add(e.Snapshot.GeometryVersion);
            item.GeometryChanged += geometry =>
            {
                handleFirst.Add(geometry.Anchor.X);
                if (geometry.Anchor.X == 2) item.UpdateGeometry(MeasurementGeometry.Point(new(3, 0)));
            };
            item.GeometryChanged += geometry => handleSecond.Add(geometry.Anchor.X);
            item.UpdateGeometry(MeasurementGeometry.Point(new(1, 0)));
        });
        Assert.Equal(new long[] { 1, 2, 3 }, first);
        Assert.Equal(first, second);
        Assert.Equal(new double[] { 1, 2, 3 }, handleFirst);
        Assert.Equal(handleFirst, handleSecond);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReentrantCompletionRemovalOrClosurePreservesTerminalOrder(bool close)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var observed = new List<string>();
        viewer.MeasurementCompleted += (_, e) =>
        {
            if (close) viewer.Host.Window.CloseProgrammatically();
            else e.Measurement.Dispose();
        };
        viewer.MeasurementCompleted += (_, _) => observed.Add("completed");
        viewer.MeasurementRemoved += (_, _) => observed.Add("removed");
        viewer.MeasurementChanged += (_, _) => observed.Add("changed");
        viewer.Closed += (_, _) => observed.Add("closed");
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements)
                .CreateMeasurement(MeasurementGeometry.Point(new()));
            item.Complete();
        });
        await viewer.DisposeAsync();
        Assert.Equal(new[] { "completed", "removed", "closed" }, observed);
    }

    [Fact]
    public async Task ResultInvalidationCannotOvertakePublishedResultForLaterSubscribers()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var first = new List<string>();
        var second = new List<string>();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements)
                .CreateMeasurement(MeasurementGeometry.Point(new()));
            item.Complete();
            item.ResultChanged += result =>
            {
                first.Add(result == null ? "invalid" : "published");
                if (result != null) item.UpdateGeometry(MeasurementGeometry.Point(new(1, 0)));
            };
            item.ResultChanged += result => second.Add(result == null ? "invalid" : "published");
            item.PublishResult(new MeasurementSampleResult(item.Id, 0,
                new(1, new(2, 1, 2, FramePixelFormat.Gray8), null), MeasurementQuery.Pixel,
                [new(0, 0)], [new(FramePixelFormat.Gray8, 7, 0, 0, 0, 255)]));
        });
        Assert.Equal(new[] { "published", "invalid" }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ClosureDuringFirstChangeSubscriberRetainsAllCapturedNotifications()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var order = new List<string>();
        viewer.MeasurementChanged += (_, _) => viewer.Host.Window.CloseProgrammatically();
        viewer.MeasurementChanged += (_, e) => order.Add($"changed:{e.Snapshot.GeometryVersion}");
        viewer.MeasurementRemoved += (_, _) => order.Add("removed");
        viewer.Closed += (_, _) => order.Add("closed");
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements)
                .CreateMeasurement(MeasurementGeometry.Point(new()));
            item.Complete();
            item.GeometryChanged += _ => order.Add("geometry");
            item.UpdateGeometry(MeasurementGeometry.Point(new(1, 0)));
        });
        await viewer.DisposeAsync();
        Assert.Equal(new[] { "changed:1", "geometry", "removed", "closed" }, order);
    }
}
