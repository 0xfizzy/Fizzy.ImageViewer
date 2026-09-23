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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var tool = new Tool(); viewer.RegisterMeasurementTool(tool);
            viewer.StartMeasurement(tool.Id); viewer.Interaction.ImageDown(2, 3);
            switch (operation)
            {
                case "all": viewer.Layers.Clear(); break;
                case "measurements": viewer.Layers.Measurements.Clear(); break;
                case "hide": viewer.Layers.Measurements.IsVisible = false; break;
                default: viewer.Layers.Measurements.IsHitTestVisible = false; break;
            }
            Assert.Equal(1, tool.Cancellations);
            Assert.Equal(InteractionMode.Idle, viewer.Interaction.Mode);
            Assert.False(viewer.Layers.InputSuppressed);
            Assert.Empty(viewer.WindowForTests.MeasurementOverlay.Canvas.Children);
        });
    }

    [Fact]
    public async Task ClearFinishesOtherLayersAfterFailureAndRejectsNewSessionsDuringCleanup()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var tool = new Tool(); viewer.RegisterMeasurementTool(tool);
            var failure = new InvalidOperationException("cancel failed");
            tool.Cancelled = context =>
            {
                Assert.Throws<InvalidOperationException>(() => viewer.StartMeasurement(tool.Id));
                Assert.Throws<InvalidOperationException>(() => context.CreateMeasurement(MeasurementGeometry.Point(new())));
                viewer.ClearShapes(); // Reentrant bulk cleanup is idempotent.
                throw failure;
            };
            viewer.StartMeasurement(tool.Id); viewer.Interaction.ImageDown(2, 3);
            var layer = viewer.Layers.CreateLayer("after measurements");
            using var batch = layer.AddBatch([new CircleElement(new(), 2, Brushes.Red)]);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(viewer.ClearShapes));
            Assert.Throws<ObjectDisposedException>(() => batch.Replace([]));
            Assert.Equal(1, tool.Cancellations);
            Assert.Empty(viewer.WindowForTests.MeasurementOverlay.Canvas.Children);
            Assert.Equal(InteractionMode.Idle, viewer.Interaction.Mode);
        });
    }

    [Fact]
    public async Task RoutedShapeInputIsOwnedOnlyByTheCoordinator()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
            var shape = item.PrimaryVisual;
            void Click() => shape.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.MouseDownEvent });
            Click(); Assert.Same(shape, viewer.Interaction.SelectedShape);
            viewer.Interaction.ImageDown(10, 10); Assert.Null(viewer.Interaction.SelectedShape);
            viewer.Interaction.Dispose();
            Click(); Assert.Null(viewer.Interaction.SelectedShape);
        });
    }
}
