using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementHandleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiredHandleDoesNotRetainItsCollectionOrOverlayEvenWhenCleanupFails(bool fail)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var retained = await viewer.Host.Window.Dispatcher.InvokeAsync(() => CreateRetiredHandle(viewer, fail));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(retained.Owner.IsAlive);
        Assert.False(retained.Overlay.IsAlive);
        Assert.False(retained.Runtime.IsAlive);
        Assert.Equal(new Point(1, 2), retained.Handle.Geometry.Anchor);
        retained.Handle.Dispose();
        await viewer.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => retained.Handle.Geometry);
        retained.Handle.GeometryChanged -= IgnoreGeometry;
        retained.Handle.Dispose();
        GC.KeepAlive(retained.Handle);
    }

    private static void IgnoreGeometry(object? sender, MeasurementGeometry geometry) { }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (IMeasurement Handle, WeakReference Owner, WeakReference Overlay, WeakReference Runtime) CreateRetiredHandle(Viewer viewer, bool fail)
    {
        var layers = new Fizzy.ImageViewer.Layers.ViewerLayers(new System.Windows.Media.ScaleTransform());
        var runtime = new MeasurementRuntime(viewer.Host.Control.Runtime.MeasurementRuntime.Lifetime, viewer.Host.Window.Dispatcher, viewer.Host.Control.Runtime.Queries, NullLogger.Instance);
        var owner = new MeasurementCollection(layers.Measurements, runtime);
        var context = new MeasurementCreationContext(owner, runtime, () => null);
        var handle = context.CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
        handle.Complete();
        if (fail) handle.OnDispose(() => throw new InvalidOperationException("cleanup failed"));
        if (fail) Assert.Throws<AggregateException>(handle.Dispose);
        else handle.Dispose();
        Assert.Empty(layers.Measurements.Overlay.Canvas.Children);
        return (handle, new(owner), new(layers.Measurements.Overlay), new(runtime));
    }

    [Fact]
    public async Task RetainedHandleUpdatesAndDisposesFromWorkerWithStaNotifications()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        MeasurementEventArgs? completed = null;
        viewer.MeasurementCompleted += (_, e) => completed = e;
        viewer.ActivateMeasurementTool(MeasurementToolIds.Point);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => viewer.Host.Control.Runtime.Interaction.ImageDown(1, 2));
        var handle = completed!.Measurement;
        int changes = 0, disposed = 0;
        await Task.Run(() =>
        {
            handle.GeometryChanged += (_, geometry) =>
            {
                viewer.Host.Window.Dispatcher.VerifyAccess();
                Assert.Equal(new Point(3, 4), Assert.IsType<PointMeasurementGeometry>(geometry).Position);
                changes++;
            };
            handle.OnDispose(() => { viewer.Host.Window.Dispatcher.VerifyAccess(); disposed++; });
            handle.UpdateGeometry(MeasurementGeometry.Point(new(3, 4)));
            Assert.Equal(new Point(3, 4), Assert.IsType<PointMeasurementGeometry>(handle.Geometry).Position);
            Assert.Equal(1, handle.GeometryVersion);
            Assert.True(handle.IsComplete);
        });
        Assert.Equal(1, changes);
        Assert.Equal(new Point(1, 2), Assert.IsType<PointMeasurementGeometry>(completed.Snapshot.Geometry).Position);
        await Task.Run(handle.Dispose);
        Assert.True(handle.IsDisposed);
        EventHandler<MeasurementGeometry> geometryHandler = (_, _) => { };
        EventHandler<MeasurementQueryResult?> queryHandler = (_, _) => { };
        Assert.Throws<ObjectDisposedException>(() => handle.GeometryChanged += geometryHandler);
        Assert.Throws<ObjectDisposedException>(() => handle.QueryResultChanged += queryHandler);
        handle.GeometryChanged -= geometryHandler;
        handle.QueryResultChanged -= queryHandler;
        Assert.Equal(1, disposed);
        Assert.Throws<ObjectDisposedException>(() => handle.UpdateGeometry(MeasurementGeometry.Point(new())));
        await viewer.DisposeAsync();
        handle.Dispose();
        Assert.Throws<ObjectDisposedException>(() => handle.GeometryChanged += geometryHandler);
        Assert.Throws<ObjectDisposedException>(() => handle.QueryResultChanged += queryHandler);
        handle.GeometryChanged -= geometryHandler;
        handle.QueryResultChanged -= queryHandler;
        Assert.Equal(completed.Snapshot.Id, handle.Id);
        Assert.True(handle.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => handle.Geometry);
    }

    [Fact]
    public async Task AsyncToolWorkCanUsePreviewUntilSessionEnds()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        IMeasurement? preview = null;
        viewer.RegisterMeasurementTool(new TestMeasurementTool(item => preview = item));
        viewer.ActivateMeasurementTool("async");
        await Task.Run(() => { preview!.UpdateGeometry(MeasurementGeometry.Point(new(5, 6))); preview.Complete(); });
        Assert.True(preview!.IsComplete);
        viewer.EndInteraction();
        Assert.False(preview.IsDisposed);
        viewer.ActivateMeasurementTool("async");
        viewer.EndInteraction();
        Assert.True(preview.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Task.Run(() => preview.Complete()));
        await viewer.DisposeAsync();
        Assert.True(preview.IsDisposed);
        preview.Dispose();
    }

    private sealed class TestMeasurementTool(Action<IMeasurement> created) : IMeasurementTool
    {
        public string Id => "async";
        public string DisplayName => "Async";
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        {
            created(context.CreateMeasurement(MeasurementGeometry.Point(new())));
            return new Session();
        }
        private sealed class Session : IMeasurementToolSession
        {
            public MeasurementClickResult OnClick(Point point) => MeasurementClickResult.Continue;
            public void OnMouseMove(Point point) { }
            public void Cancel() { }
            public void Dispose() { }
        }
    }
}
