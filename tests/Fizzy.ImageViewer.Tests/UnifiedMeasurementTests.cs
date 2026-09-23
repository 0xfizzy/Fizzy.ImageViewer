using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class UnifiedMeasurementTests
{
    private static Viewer Create() => new(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
    private sealed class Runtime(Dispatcher dispatcher) : IQueryRuntime
    {
        public TimeSpan Now { get; set; }
        public IDisposable StartTicks(Action tick) => new QuerySubscription(() => { });
        public Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token) => Task.Run(action, token);
        public Task PublishAsync(Action action, CancellationToken token) => dispatcher.InvokeAsync(action, DispatcherPriority.Normal, token).Task;
    }
    private sealed class Source : IFramePixelSource
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail;
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> points, CancellationToken ct)
            => ValueTask.FromResult(points.ToArray().Select(_ => new PixelSample(FramePixelFormat.Gray8, 7, 0, 0, 0, 255)).ToArray());
        public async ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct)
        {
            Entered.TrySetResult(); await Release.Task;
            if (Fail) throw new InvalidOperationException("query failure");
            return new(FramePixelFormat.Gray8, [new(region.Width * region.Height, 7, 7, 7)]);
        }
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Harness : IDisposable
    {
        public readonly OverlayLayer Overlay = new();
        public readonly FrameLease Frame;
        public readonly Runtime Runtime;
        public readonly PixelQueryScheduler Queries;
        public readonly MeasurementContext Context;
        public Harness(Source? source = null)
        {
            Frame = source == null ? ImageFrame.Copy(new(4, 4, 4, FramePixelFormat.Gray8), Enumerable.Range(0, 16).Select(x => (byte)x).ToArray()).Transfer()
                : new ImageFrame(new FrameStorage(new(4, 4, 4, FramePixelFormat.Gray8), new byte[16], () => { }, 0, source)).Transfer();
            Frame.Info = new(42, Frame.Descriptor, null);
            Runtime = new(Overlay.Dispatcher);
            Queries = new(() => Frame.Acquire(), NullLogger.Instance, Runtime);
            Context = new(Overlay, () => Frame.Acquire(), Queries, NullLogger.Instance);
        }
        public void Dispose() { Context.Shutdown(); Queries.Dispose(); Frame.Dispose(); }
    }
    private sealed class RoiTool : IMeasurementTool
    {
        public string Id => "custom-roi";
        public string DisplayName => "Custom ROI";
        public IMeasurement? Item;
        public bool OnClick(Point point, IMeasurementToolContext context)
        {
            if (Item == null) { Item = context.CreateMeasurement(MeasurementGeometry.Rectangle(point, point), new() { Query = MeasurementQuery.RegionStatistics }); return false; }
            Item.UpdateGeometry(MeasurementGeometry.Rectangle(Item.Geometry.Start, point)); Item.Complete(); return true;
        }
        public void OnMouseMove(Point point, IMeasurementToolContext context) { }
        public void Cancel(IMeasurementToolContext context) { }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuiltInAndCustomRoiShareNotificationsEditingQueriesAndLabels(bool custom)
    {
        await using var viewer = Create();
        var tool = new RoiTool();
        MeasurementItem? item = null;
        var ready = new TaskCompletionSource<MeasurementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0; var removed = 0;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.RegisterMeasurementTool(tool);
            viewer.MeasurementCompleted += (_, e) => { completed++; Assert.Equal(ShapeType.Rectangle, e.Snapshot.Geometry.Kind); };
            viewer.MeasurementRemoved += (_, _) => removed++;
            viewer.StartMeasurement(custom ? tool.Id : MeasurementToolIds.ROI);
            viewer.Interaction.ImageDown(0, 0); viewer.Interaction.ImageDown(2, 2);
            var shape = viewer.WindowForTests.MeasurementOverlay.Canvas.Children.OfType<System.Windows.Shapes.Rectangle>().Single();
            item = viewer.MeasurementContext.Find(shape)!;
            item.ResultChanged += result => { if (result != null) ready.TrySetResult(result); };
        });
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(4, 4, 4, FramePixelFormat.Gray8), Enumerable.Range(0, 16).Select(x => (byte)x).ToArray()));
        var result = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            Assert.Equal(1, completed); Assert.Equal(item!.Id, result.MeasurementId);
            Assert.Equal(2.5, Assert.Single(result.Channels).Mean); Assert.Contains("mean=2.5", item.Label.Text);
            var changes = 0;
            item.GeometryChanged += _ => changes++;
            viewer.Interaction.StartEditing(item.PrimaryVisual);
            Assert.True(viewer.Interaction.Editor.BeginDrag(new(0, 0), 1));
            viewer.Interaction.Editor.UpdateDrag(new(3, 3));
            Assert.Null(item.Result); Assert.DoesNotContain("mean=", item.Label.Text); Assert.Equal(1, changes);
            Assert.Equal(item.Geometry.Start, OverlayShapeData.Get(item.Label)!.AnchorPoint);
            Assert.Equal(2.5, result.Channels[0].Mean);
            Assert.Throws<NotSupportedException>(() => ((IList<ChannelStatistics>)result.Channels)[0] = default);
            viewer.ClearShapes(); Assert.Equal(1, removed);
        });
    }
    [Theory]
    [InlineData("edit")]
    [InlineData("remove")]
    [InlineData("fail")]
    public async Task PendingCustomRoiDoesNotPublishStaleOrFailedResults(string action)
    {
        await using var viewer = Create();
        var source = new Source(); Harness? h = null; IMeasurement? item = null; var published = 0;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            h = new(source);
            item = h.Context.CreateMeasurement(MeasurementGeometry.Rectangle(new(), new(2, 2)), new() { Query = MeasurementQuery.RegionStatistics });
            item.ResultChanged += result => { if (result != null) published++; };
            item.Complete(); h.Queries.Tick();
        });
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await viewer.UiDispatcher.InvokeAsync(() =>
            {
                if (action == "edit") item!.UpdateGeometry(MeasurementGeometry.Rectangle(new(), new(1, 1)));
                else if (action == "remove") item!.Dispose();
                else source.Fail = true;
            });
            source.Release.TrySetResult(); await h!.Queries.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            await viewer.UiDispatcher.InvokeAsync(() => { Assert.Equal(0, published); Assert.Null(item!.Result); });
        }
        finally { source.Release.TrySetResult(); await h!.Queries.Completion; await viewer.UiDispatcher.InvokeAsync(h.Dispose); }
    }
    [Theory]
    [InlineData(MeasurementQuery.Pixel)]
    [InlineData(MeasurementQuery.LineProfile)]
    public async Task QuerySnapshotsOwnSamplesAndInvalidateImmediately(MeasurementQuery query)
    {
        await using var viewer = Create(); Harness? h = null; IMeasurement? item = null;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            h = new();
            item = h.Context.CreateMeasurement(query == MeasurementQuery.Pixel ? MeasurementGeometry.Point(new(1, 1))
                : MeasurementGeometry.Line(new(), new(3, 0)), new() { Query = query });
            item.Complete(); h.Queries.Tick();
        });
        await h!.Queries.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            using (h)
            {
                var result = Assert.IsType<MeasurementResult>(item!.Result);
                Assert.Equal(42, result.Frame.FrameId); Assert.Equal(item.GeometryVersion, result.GeometryVersion);
                Assert.Equal(query == MeasurementQuery.Pixel ? 5 : 0, result.Samples[0].Gray);
                var version = item.GeometryVersion; item.UpdateGeometry(item.Geometry); Assert.Equal(version, item.GeometryVersion);
                Assert.Same(result, item.Result);
                item.UpdateGeometry(query == MeasurementQuery.Pixel ? MeasurementGeometry.Point(new(2, 2)) : MeasurementGeometry.Line(new(), new(1, 0)));
                Assert.Null(item.Result); Assert.Equal(version + 1, item.GeometryVersion);
                Assert.Throws<NotSupportedException>(() => ((IList<PixelSample>)result.Samples)[0] = default);
                Assert.Equal(query == MeasurementQuery.Pixel ? 5 : 0, result.Samples[0].Gray);
            }
        });
    }
    [Fact]
    public async Task ExpiredAndFailedQueriesClearPreviouslyPublishedResult()
    {
        await using var viewer = Create(); var source = new Source(); source.Release.SetResult();
        Harness? h = null; IMeasurement? item = null; var invalidations = 0;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            h = new(source);
            item = h.Context.CreateMeasurement(MeasurementGeometry.Rectangle(new(), new(2, 2)), new() { Query = MeasurementQuery.RegionStatistics });
            item.ResultChanged += result => { if (result == null) invalidations++; };
            item.Complete(); h.Queries.Tick();
        });
        await h!.Queries.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            Assert.NotNull(item!.Result);
            h.Frame.Info = new(43, h.Frame.Descriptor, null);
            h.Runtime.Now = TimeSpan.FromSeconds(10); source.Fail = true; h.Queries.Tick();
            Assert.Null(item.Result); Assert.Equal(1, invalidations);
            Assert.DoesNotContain("mean=", ((MeasurementItem)item).Label.Text);
        });
        await h.Queries.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await viewer.UiDispatcher.InvokeAsync(() => { using (h) { Assert.Null(item!.Result); Assert.Equal(1, invalidations); } });
    }

    [Fact]
    public async Task CircleEditsWriteBackRadiusAndCenterAndCallbacksCanRemoveItem()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Circle(new(2, 3), 4));
            item.Complete(); viewer.Interaction.StartEditing(item.PrimaryVisual);
            var editor = viewer.Interaction.Editor;
            Assert.True(editor.BeginDrag(new(2, 3), 1)); editor.UpdateDrag(new(5, 6)); editor.EndDrag();
            Assert.Equal(new Point(5, 6), item.Geometry.Start); Assert.Equal(4, item.Geometry.Radius);
            Assert.True(editor.BeginDrag(new(9, 6), 1)); editor.UpdateDrag(new(5, 9)); editor.EndDrag();
            Assert.Equal(3, item.Geometry.Radius);
            var ellipse = (System.Windows.Media.EllipseGeometry)((System.Windows.Shapes.Path)item.PrimaryVisual).Data;
            Assert.Equal(3, ellipse.RadiusX);
            item.GeometryChanged += _ => throw new Exception("isolated");
            item.GeometryChanged += _ => item.Dispose();
            Assert.True(editor.BeginDrag(new(5, 6), 1)); editor.UpdateDrag(new(7, 8));
            Assert.True(item.IsDisposed); Assert.False(editor.IsEditing);
        });
    }
    [Fact]
    public async Task InvalidCombinationsNeverAttachAndClearRejectsCreationFromDisposal()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var context = viewer.MeasurementContext;
            Assert.Throws<ArgumentException>(() => context.CreateMeasurement(MeasurementGeometry.Circle(new(), 1), new() { Query = MeasurementQuery.RegionStatistics }));
            Assert.Throws<ArgumentException>(() => context.CreateMeasurement(MeasurementGeometry.Point(new()), new() { ShowLineProfile = true }));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(new(), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(new(), double.MaxValue));
            Assert.Empty(viewer.WindowForTests.MeasurementOverlay.Canvas.Children);
            var item = context.CreateMeasurement(MeasurementGeometry.Point(new())); item.Complete();
            item.OnDispose(() => Assert.Throws<InvalidOperationException>(() => context.CreateMeasurement(MeasurementGeometry.Point(new()))));
            viewer.ClearShapes();
        });
    }
}
