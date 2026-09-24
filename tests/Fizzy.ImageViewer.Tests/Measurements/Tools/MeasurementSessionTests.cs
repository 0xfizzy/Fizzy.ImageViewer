using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Measurements.BuiltIn;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementSessionTests
{
    private sealed class FactoryTool(Func<IMeasurementToolContext, IMeasurementToolSession> factory) : IMeasurementTool
    {
        public string Id => "factory";
        public string DisplayName => Id;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => factory(context);
    }

    [Fact]
    public async Task SameRegistrationHasIndependentStateAcrossViewers()
    {
        await using var first = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await using var second = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var tool = new LengthTool();
        first.UnregisterMeasurementTool(tool.Id);
        second.UnregisterMeasurementTool(tool.Id);
        first.RegisterMeasurementTool(tool);
        second.RegisterMeasurementTool(tool);
        MeasurementEventArgs? a = null, b = null;
        first.MeasurementCompleted += (_, e) => a = e;
        second.MeasurementCompleted += (_, e) => b = e;
        first.StartMeasurement(tool.Id);
        second.StartMeasurement(tool.Id);
        await first.Host.Window.Dispatcher.InvokeAsync(() => first.Host.Interaction.ImageDown(1, 2));
        await second.Host.Window.Dispatcher.InvokeAsync(() => second.Host.Interaction.ImageDown(10, 20));
        await first.Host.Window.Dispatcher.InvokeAsync(() => first.Host.Interaction.ImageDown(3, 4));
        await second.Host.Window.Dispatcher.InvokeAsync(() => second.Host.Interaction.ImageDown(30, 40));
        Assert.Equal(MeasurementGeometry.Line(new(1, 2), new(3, 4)), a!.Snapshot.Geometry);
        Assert.Equal(MeasurementGeometry.Line(new(10, 20), new(30, 40)), b!.Snapshot.Geometry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededFactoryCannotCancelReplacement(bool throws)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            int factories = 0, cancellations = 0;
            IMeasurementToolContext? oldContext = null;
            IMeasurement? oldPreview = null, survivor = null;
            var failure = new InvalidOperationException("factory failed");
            var tool = new FactoryTool(context =>
            {
                if (++factories == 1)
                {
                    oldContext = context;
                    oldPreview = context.CreateMeasurement(MeasurementGeometry.Point(new()));
                    viewer.StartMeasurement("factory");
                    if (throws) throw failure;
                }
                else survivor = context.CreateMeasurement(MeasurementGeometry.Point(new(5, 6)));
                return new TestMeasurementSession(_ => false, _ => { }, () => cancellations++);
            });
            viewer.RegisterMeasurementTool(tool);
            if (throws) Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => viewer.StartMeasurement(tool.Id)));
            else viewer.StartMeasurement(tool.Id);
            Assert.Equal(tool.Id, viewer.Host.Interaction.ActiveId);
            Assert.True(viewer.Layers.Collection.InputSuppressed);
            Assert.True(oldPreview!.IsDisposed);
            Assert.False(survivor!.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => oldContext!.CreateMeasurement(MeasurementGeometry.Point(new())));
            Assert.Equal(throws ? 0 : 1, cancellations);
            viewer.EndInteraction();
            Assert.True(survivor.IsDisposed);
            Assert.Equal(throws ? 1 : 2, cancellations);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidFactoryCleansItsPreviewAndRestoresInput(bool returnsNull)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            IMeasurement? preview = null;
            IMeasurementToolContext? retained = null;
            var tool = new FactoryTool(context =>
            {
                retained = context;
                preview = context.CreateMeasurement(MeasurementGeometry.Point(new()));
                if (returnsNull) return null!;
                throw new InvalidOperationException("factory failed");
            });
            viewer.RegisterMeasurementTool(tool);
            Assert.Throws<InvalidOperationException>(() => viewer.StartMeasurement(tool.Id));
            Assert.True(preview!.IsDisposed);
            Assert.Null(viewer.Host.Interaction.ActiveId);
            Assert.False(viewer.Layers.Collection.InputSuppressed);
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children);
            Assert.Throws<ObjectDisposedException>(() => retained!.CreateMeasurement(MeasurementGeometry.Point(new())));
        });
    }

    [Fact]
    public async Task LayerOwnsCleanupWithoutAnInteractionCoordinator()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var layers = new ViewerLayers(Transform.Identity);
            var layer = layers.Measurements;
            int acquisitions = 0, released = 0;
            using var queries = new PixelQueryScheduler(() => { acquisitions++; return null; },
                NullLogger.Instance, new DispatcherQueryRuntime(layer.Overlay.Dispatcher));
            var owner = new MeasurementCollection(layer, new ViewerLifetime(), layer.Overlay.Dispatcher, () => null, queries, NullLogger.Instance);
            var creation = new MeasurementCreationContext(owner);
            var completed = creation.CreateMeasurement(MeasurementGeometry.Point(new()), new() { Query = MeasurementQueryKind.Pixel });
            completed.Complete();
            completed.OnDispose(() =>
            {
                released++;
                Assert.Throws<InvalidOperationException>(() => creation.CreateMeasurement(MeasurementGeometry.Point(new())));
                layer.Clear();
                throw new InvalidOperationException("cleanup failure");
            });
            var preview = creation.CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
            preview.OnDispose(() => released++);
            queries.Tick();
            Assert.Equal(1, acquisitions);
            layer.Clear();
            Assert.True(completed.IsDisposed);
            Assert.True(preview.IsDisposed);
            Assert.Equal(2, released);
            Assert.Empty(layer.Overlay.Canvas.Children);
            Assert.False(owner.Contains((MeasurementItem)completed));
            queries.Tick();
            Assert.Equal(1, acquisitions); // No query subscription survived the layer clear.
            owner.Shutdown();
            layers.Collection.Close();
        });
    }

    [Fact]
    public async Task LayerStillReleasesCompletedMeasurementsAfterCoordinatorDisposal()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var creation = new MeasurementCreationContext(viewer.Host.Measurements);
            var item = creation.CreateMeasurement(MeasurementGeometry.Point(new()));
            item.Complete();
            int released = 0;
            item.OnDispose(() => released++);
            viewer.Host.Interaction.Dispose();
            viewer.Layers.Measurements.Clear();
            Assert.True(item.IsDisposed);
            Assert.Equal(1, released);
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children);
        });
    }
}
