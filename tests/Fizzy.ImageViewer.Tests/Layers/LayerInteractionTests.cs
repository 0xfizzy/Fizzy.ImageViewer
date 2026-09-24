using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class LayerInteractionTests
{
    private sealed class Tool : IMeasurementTool
    {
        public string Id => "cancellation";
        public string DisplayName => Id;
        public int Cancellations;
        public Action<IMeasurementToolContext>? Cancelled;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
            => new TestMeasurementSession(point => OnClick(point, context),
                point => OnMouseMove(point, context), () => Cancel(context));
        public bool OnClick(Point point, IMeasurementToolContext context)
        { context.CreateMeasurement(MeasurementGeometry.Point(point)); return false; }
        public void OnMouseMove(Point point, IMeasurementToolContext context) { }
        public void Cancel(IMeasurementToolContext context) { Cancellations++; Cancelled?.Invoke(context); }
    }

    [Theory]
    [InlineData("all")]
    [InlineData("measurements")]
    [InlineData("hide")]
    [InlineData("hit-test")]
    public async Task LayerPolicyChangesCancelTheSessionExactlyOnce(string operation)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var tool = new Tool(); viewer.RegisterMeasurementTool(tool);
            viewer.ActivateMeasurementTool(tool.Id); viewer.Host.Interaction.ImageDown(2, 3);
            switch (operation)
            {
                case "all": viewer.Layers.ClearContents(); break;
                case "measurements": viewer.Layers.Measurements.Clear(); break;
                case "hide": viewer.Layers.Measurements.IsVisible = false; break;
                default: viewer.Layers.Measurements.IsHitTestVisible = false; break;
            }
            Assert.Equal(1, tool.Cancellations);
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.Collection.InputSuppressed);
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children);
        });
    }

    [Fact]
    public async Task ClearFinishesOtherLayersAfterFailureAndRejectsNewSessionsDuringCleanup()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var tool = new Tool(); viewer.RegisterMeasurementTool(tool);
            var failure = new InvalidOperationException("cancel failed");
            tool.Cancelled = context =>
            {
                Assert.Throws<InvalidOperationException>(() => viewer.ActivateMeasurementTool(tool.Id));
                Assert.Throws<InvalidOperationException>(() => context.CreateMeasurement(MeasurementGeometry.Point(new())));
                viewer.Layers.ClearContents(); // Reentrant bulk cleanup is idempotent.
                throw failure;
            };
            viewer.ActivateMeasurementTool(tool.Id); viewer.Host.Interaction.ImageDown(2, 3);
            var layer = viewer.Layers.CreateDrawingLayer("after measurements");
            using var batch = layer.Add([new CircleElement(new(), 2, Brushes.Red)]);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(viewer.Layers.ClearContents));
            Assert.Throws<ObjectDisposedException>(() => batch.Replace([]));
            Assert.Equal(1, tool.Cancellations);
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children);
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
        });
    }

    [Fact]
    public async Task RoutedShapeInputIsOwnedOnlyByTheCoordinator()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
            var shape = item.Presentation.PrimaryVisual;
            void Click() => shape.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.MouseDownEvent });
            Click(); Assert.Same(item, viewer.Host.Interaction.SelectedMeasurement);
            viewer.Host.Interaction.ImageDown(10, 10); Assert.Null(viewer.Host.Interaction.SelectedMeasurement);
            viewer.Host.Interaction.Dispose();
            Click(); Assert.Null(viewer.Host.Interaction.SelectedMeasurement);
        });
    }
}
