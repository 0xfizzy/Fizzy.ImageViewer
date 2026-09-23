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
        private IMeasurement? _preview;
        public bool OnClick(Point point, IMeasurementToolContext context)
        {
            if (_preview == null)
            {
                _preview = context.CreateMeasurement(MeasurementGeometry.Rectangle(point, point),
                    new() { Query = MeasurementQuery.RegionStatistics });
                return false;
            }
            var item = _preview;
            _preview = null;
            item.UpdateGeometry(MeasurementGeometry.Rectangle(item.Geometry.Start, point));
            item.Complete();
            return true;
        }
        public void OnMouseMove(Point point, IMeasurementToolContext context) { }
        public void Cancel(IMeasurementToolContext context) => _preview = null;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuiltInAndCustomResultsAreObservableThroughThePublicFacade(bool custom)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        IViewerAPI api = viewer;
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
            api.StartMeasurement(custom ? tool.Id : MeasurementToolIds.ROI);
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
        Assert.Equal(2.5, Assert.Single(published.Result.Channels).Mean);

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
        Assert.Equal(2.5, published.Result.Channels[0].Mean);
        await Task.Run(published.Handle.Dispose);
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
        viewer.MeasurementChanged += (_, e) => { changed++; e.Handle.Dispose(); };
        viewer.MeasurementRemoved += (_, _) => removed++;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)viewer.Host.Measurements.CreateMeasurement(MeasurementGeometry.Point(new(0, 0)));
            item.Complete();
            item.UpdateGeometry(MeasurementGeometry.Point(new(1, 1)));
            Assert.True(item.IsDisposed);
            Assert.False(viewer.Host.Measurements.Contains(item));
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children);
        });
        Assert.Equal(1, changed);
        Assert.Equal(1, removed);
    }
}
