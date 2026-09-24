using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Measurements.Editing;
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
        public readonly ViewerLayers Layers = new(System.Windows.Media.Transform.Identity);
        public MeasurementOverlay Overlay => Layers.Measurements.Overlay;
        public readonly FrameLease Frame;
        public readonly Runtime Runtime;
        public readonly PixelQueryScheduler Queries;
        public readonly MeasurementCollection Context;
        public readonly MeasurementRuntime MeasurementsRuntime;
        public Harness(Source? source = null)
        {
            Frame = source == null ? ImageFrame.Copy(new(4, 4, 4, FramePixelFormat.Gray8), Enumerable.Range(0, 16).Select(x => (byte)x).ToArray()).Transfer()
                : new ImageFrame(new FrameStorage(new(4, 4, 4, FramePixelFormat.Gray8), new byte[16], () => { }, 0, source)).Transfer();
            Frame.Info = new(42, Frame.Descriptor, null);
            Runtime = new(Overlay.Dispatcher);
            Queries = new(() => Frame.Acquire(), NullLogger.Instance, Runtime);
            MeasurementsRuntime = new(new ViewerLifetime(), Overlay.Dispatcher, Queries, NullLogger.Instance);
            Context = new(Layers.Measurements, MeasurementsRuntime, NullLogger.Instance);
        }
        public void Dispose() { Context.Shutdown(); Queries.Dispose(); Frame.Dispose(); }
    }
    private sealed class RoiTool : IMeasurementTool
    {
        public string Id => "custom-roi";
        public string DisplayName => "Custom ROI";
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => new Session(context);

        private sealed class Session(IMeasurementToolContext context) : IMeasurementToolSession
        {
            private IMeasurement? _item;
            public MeasurementClickResult OnClick(Point point)
            {
                if (_item == null)
                {
                    _item = context.CreateMeasurement(MeasurementGeometry.Rectangle(point, point),
                        new() { Query = MeasurementQueryKind.RegionStatistics });
                    return MeasurementClickResult.Continue;
                }
                _item.UpdateGeometry(MeasurementGeometry.Rectangle(Assert.IsType<RectangleMeasurementGeometry>(_item.Geometry).TopLeft, point));
                _item.Complete();
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
    public async Task BuiltInAndCustomRoiShareNotificationsEditingQueriesAndLabels(bool custom)
    {
        await using var viewer = Create();
        var tool = new RoiTool();
        MeasurementItem? item = null;
        var ready = new TaskCompletionSource<MeasurementQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0; var removed = 0;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.RegisterMeasurementTool(tool);
            viewer.MeasurementCompleted += (_, e) => { completed++; Assert.Equal(MeasurementGeometryKind.Rectangle, e.Snapshot.Geometry.Kind); };
            viewer.MeasurementRemoved += (_, _) => removed++;
            viewer.ActivateMeasurementTool(custom ? tool.Id : MeasurementToolIds.RectangleRoi);
            viewer.Host.Interaction.ImageDown(0, 0); viewer.Host.Interaction.ImageDown(2, 2);
            var shape = viewer.Host.Window.MeasurementOverlay.Canvas.Children.OfType<System.Windows.Shapes.Rectangle>().Single();
            item = viewer.Host.Measurements.Find(shape)!;
            item.QueryResultChanged += result => { if (result != null) ready.TrySetResult(result); };
        });
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(4, 4, 4, FramePixelFormat.Gray8), Enumerable.Range(0, 16).Select(x => (byte)x).ToArray()));
        var result = Assert.IsType<MeasurementRegionResult>(await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.Equal(1, completed); Assert.Equal(item!.Id, result.MeasurementId);
            Assert.Equal(2.5, Assert.Single(result.Channels).Mean); Assert.Contains("mean=2.5", item.Presentation.Label.Text);
            var changes = 0;
            item.GeometryChanged += _ => changes++;
            viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(item.Presentation.PrimaryVisual));
            Assert.True(viewer.Host.Interaction.Editor.BeginDrag(new(0, 0), 1));
            viewer.Host.Interaction.Editor.UpdateDrag(new(3, 3));
            Assert.Null(item.QueryResult); Assert.DoesNotContain("mean=", item.Presentation.Label.Text); Assert.Equal(1, changes);
            Assert.Equal(Assert.IsType<RectangleMeasurementGeometry>(item.Geometry).TopLeft, MeasurementVisualData.Get(item.Presentation.Label)!.AnchorPoint);
            Assert.Equal(2.5, result.Channels[0].Mean);
            Assert.Throws<NotSupportedException>(() => ((IList<ChannelStatistics>)result.Channels)[0] = default);
            viewer.Layers.ClearContents(); Assert.Equal(1, removed);
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            h = new(source);
            item = new MeasurementCreationContext(h.Context, h.MeasurementsRuntime, h.Frame.Acquire).CreateMeasurement(MeasurementGeometry.Rectangle(new(), new(2, 2)), new() { Query = MeasurementQueryKind.RegionStatistics });
            item.QueryResultChanged += result => { if (result != null) published++; };
            item.Complete(); h.Queries.Tick();
        });
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
            {
                if (action == "edit") item!.UpdateGeometry(MeasurementGeometry.Rectangle(new(), new(1, 1)));
                else if (action == "remove") item!.Dispose();
                else source.Fail = true;
            });
            source.Release.TrySetResult(); await h!.Queries.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            await viewer.Host.Window.Dispatcher.InvokeAsync(() => { Assert.Equal(0, published); Assert.Null(item!.QueryResult); });
        }
        finally { source.Release.TrySetResult(); await h!.Queries.Completion; await viewer.Host.Window.Dispatcher.InvokeAsync(h.Dispose); }
    }
    [Theory]
    [InlineData(MeasurementQueryKind.Pixel)]
    [InlineData(MeasurementQueryKind.LineProfile)]
    public async Task QuerySnapshotsOwnSamplesAndInvalidateImmediately(MeasurementQueryKind query)
    {
        await using var viewer = Create(); Harness? h = null; IMeasurement? item = null;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            h = new();
            item = new MeasurementCreationContext(h.Context, h.MeasurementsRuntime, h.Frame.Acquire).CreateMeasurement(query == MeasurementQueryKind.Pixel ? MeasurementGeometry.Point(new(1, 1))
                : MeasurementGeometry.Line(new(), new(3, 0)), new() { Query = query == MeasurementQueryKind.Pixel ? MeasurementQueryKind.Pixel : MeasurementQueryKind.LineProfile });
            item.Complete(); h.Queries.Tick();
        });
        await h!.Queries.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using (h)
            {
                var result = Assert.IsType<MeasurementSampleResult>(item!.QueryResult);
                Assert.Equal(42, result.Frame.FrameId); Assert.Equal(item.GeometryVersion, result.GeometryVersion);
                Assert.Equal(query == MeasurementQueryKind.Pixel ? 5 : 0, result.Samples[0].Gray);
                var version = item.GeometryVersion; item.UpdateGeometry(item.Geometry); Assert.Equal(version, item.GeometryVersion);
                Assert.Same(result, item.QueryResult);
                item.UpdateGeometry(query == MeasurementQueryKind.Pixel ? MeasurementGeometry.Point(new(2, 2)) : MeasurementGeometry.Line(new(), new(1, 0)));
                Assert.Null(item.QueryResult); Assert.Equal(version + 1, item.GeometryVersion);
                Assert.Throws<NotSupportedException>(() => ((IList<PixelSample>)result.Samples)[0] = default);
                Assert.Equal(query == MeasurementQueryKind.Pixel ? 5 : 0, result.Samples[0].Gray);
            }
        });
    }
    [Fact]
    public async Task ExpiredAndFailedQueriesClearPreviouslyPublishedResult()
    {
        await using var viewer = Create(); var source = new Source(); source.Release.SetResult();
        Harness? h = null; IMeasurement? item = null; var invalidations = 0;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            h = new(source);
            item = new MeasurementCreationContext(h.Context, h.MeasurementsRuntime, h.Frame.Acquire).CreateMeasurement(MeasurementGeometry.Rectangle(new(), new(2, 2)), new() { Query = MeasurementQueryKind.RegionStatistics });
            item.QueryResultChanged += result => { if (result == null) invalidations++; };
            item.Complete(); h.Queries.Tick();
        });
        await h!.Queries.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.NotNull(item!.QueryResult);
            h.Frame.Info = new(43, h.Frame.Descriptor, null);
            h.Runtime.Now = TimeSpan.FromSeconds(10); source.Fail = true; h.Queries.Tick();
            Assert.Null(item.QueryResult); Assert.Equal(1, invalidations);
            Assert.DoesNotContain("mean=", ((MeasurementItem)item).Presentation.Label.Text);
        });
        await h.Queries.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => { using (h) { Assert.Null(item!.QueryResult); Assert.Equal(1, invalidations); } });
    }

    [Fact]
    public async Task CircleEditsWriteBackRadiusAndCenterAndCallbacksCanRemoveItem()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Circle(new(2, 3), 4));
            item.Complete(); viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(item.Presentation.PrimaryVisual));
            var editor = viewer.Host.Interaction.Editor;
            Assert.True(editor.BeginDrag(new(2, 3), 1)); editor.UpdateDrag(new(5, 6)); editor.EndDrag();
            Assert.Equal(new Point(5, 6), Assert.IsType<CircleMeasurementGeometry>(item.Geometry).Center); Assert.Equal(4, Assert.IsType<CircleMeasurementGeometry>(item.Geometry).Radius);
            Assert.True(editor.BeginDrag(new(9, 6), 1)); editor.UpdateDrag(new(5, 9)); editor.EndDrag();
            Assert.Equal(3, Assert.IsType<CircleMeasurementGeometry>(item.Geometry).Radius);
            var ellipse = (System.Windows.Media.EllipseGeometry)((System.Windows.Shapes.Path)item.Presentation.PrimaryVisual).Data;
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var context = viewer.Host.Measurements;
            Assert.Throws<ArgumentException>(() => new MeasurementCreationContext(context, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Circle(new(), 1), new() { Query = MeasurementQueryKind.RegionStatistics }));
            Assert.Throws<ArgumentException>(() => new MeasurementCreationContext(context, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Point(new()), new() { Query = MeasurementQueryKind.LineProfile, ShowProfileWindow = true }));
            Assert.Throws<ArgumentException>(() => new MeasurementCreationContext(context, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Line(new(), new(1, 1)), new() { ShowProfileWindow = true }));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(new(), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(new(), double.MaxValue));
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children);
            var item = new MeasurementCreationContext(context, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Point(new())); item.Complete();
            item.OnDispose(() => Assert.Throws<InvalidOperationException>(() => new MeasurementCreationContext(context, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Point(new()))));
            viewer.Layers.ClearContents();
        });
    }
}
