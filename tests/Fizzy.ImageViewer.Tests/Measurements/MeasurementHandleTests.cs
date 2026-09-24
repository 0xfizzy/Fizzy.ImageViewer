using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementHandleTests
{
    [Fact]
    public async Task RetainedHandleUpdatesAndDisposesFromWorkerWithStaNotifications()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        MeasurementEventArgs? completed = null;
        viewer.MeasurementCompleted += (_, e) => completed = e;
        viewer.StartMeasurement(MeasurementToolIds.Point);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => viewer.Host.Interaction.ImageDown(1, 2));
        var handle = completed!.Measurement;
        int changes = 0, disposed = 0;
        await Task.Run(() =>
        {
            handle.GeometryChanged += geometry =>
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
        Assert.Equal(1, disposed);
        Assert.Throws<ObjectDisposedException>(() => handle.UpdateGeometry(MeasurementGeometry.Point(new())));
        await viewer.DisposeAsync();
        handle.Dispose();
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
        viewer.StartMeasurement("async");
        await Task.Run(() => { preview!.UpdateGeometry(MeasurementGeometry.Point(new(5, 6))); preview.Complete(); });
        Assert.True(preview!.IsComplete);
        viewer.EndInteraction();
        Assert.False(preview.IsDisposed);
        viewer.StartMeasurement("async");
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
