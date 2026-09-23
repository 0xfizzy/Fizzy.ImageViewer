using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Editing;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementOwnershipTests
{
    [Fact]
    public async Task ModelMovesAnchorsAndRejectsForeignThreadAndDisposedUpdates()
    {
        await using var viewer = Create();
        IMeasurement? owner = null;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            owner = viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
            var item = (MeasurementItem)owner;
            viewer.WindowForTests.MeasurementOverlay.UpdateScale(2);
            owner.UpdateGeometry(MeasurementGeometry.Point(new(3, 4)));
            Assert.Equal(5.5, System.Windows.Controls.Canvas.GetLeft(item.Label));
            Assert.Equal(new Point(3, 4), item.Geometry.Start);
            Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Point(new(double.NaN, 1)));
        });
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() => owner!.UpdateGeometry(MeasurementGeometry.Point(new()))));
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            owner!.Dispose();
            Assert.Throws<ObjectDisposedException>(() => owner.UpdateGeometry(MeasurementGeometry.Point(new())));
        });
    }

    [Theory]
    [InlineData(ShapeType.Point)]
    [InlineData(ShapeType.Crosshair)]
    public async Task AnchorEditorUpdatesModelAndRetainsVisualGeometry(ShapeType kind)
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)viewer.MeasurementContext.CreateMeasurement(kind == ShapeType.Point
                ? MeasurementGeometry.Point(new(1, 2)) : MeasurementGeometry.Crosshair(new(1, 2)));
            var shape = (System.Windows.Shapes.Path)item.PrimaryVisual;
            var geometry = shape.Data;
            using var target = ShapeEditorFactory.Create(shape, item)!;
            target.BeginDrag(0); target.Update(new(5, 6)); target.EndDrag();
            viewer.WindowForTests.MeasurementOverlay.UpdateScale(2);
            Assert.Equal(new Point(5, 6), item.Geometry.Start);
            Assert.Equal(new Point(5, 6), Assert.Single(target.Points));
            Assert.Same(geometry, shape.Data);
        });
    }

    private static Viewer Create() => new(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
    private sealed class Resource(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    private sealed class Tool : IMeasurementTool
    {
        public string Id => "custom";
        public string DisplayName => "Custom";
        public int Disposals;
        public bool Finish;
        public bool OnClick(Point point, IMeasurementToolContext context)
        {
            var scope = context.CreateMeasurement(MeasurementGeometry.Point(new()));

            scope.AddResource(new Resource(() => Disposals++));
            if (Finish) scope.Complete();
            return Finish;
        }
        public void OnMouseMove(Point point, IMeasurementToolContext context) { }
        // Intentionally does not release anything: the manager must provide the guarantee.
        public void Cancel(IMeasurementToolContext context) => throw new InvalidOperationException("Plugin cancel failed");
    }

    [Fact]
    public async Task CancelReleasesPreviewEvenWhenToolThrowsButRetainsCompletedResult()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var tool = new Tool { Finish = true };
            viewer.RegisterMeasurementTool(tool);
            viewer.StartMeasurement(tool.Id); viewer.Interaction.ImageDown(2, 3);
            tool.Finish = false;
            viewer.StartMeasurement(tool.Id); viewer.Interaction.ImageDown(4, 5);
            Assert.Throws<InvalidOperationException>(() => viewer.CancelMeasurement());
            Assert.Equal(1, tool.Disposals);
            Assert.Equal(2, viewer.WindowForTests.MeasurementOverlay.Canvas.Children.Count);
            viewer.ClearShapes();
            Assert.Equal(2, tool.Disposals);
        });
    }

    [Fact]
    public async Task RemovingAssociatedVisualCleansOnlyItsOwnerDespiteFailuresAndReentry()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var ctx = viewer.MeasurementContext;
            var a = (MeasurementItem)ctx.CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
            var b = (MeasurementItem)ctx.CreateMeasurement(MeasurementGeometry.Point(new(3, 4)));
            var primary = a.PrimaryVisual; var label = a.Label; a.Complete(); b.Complete();
            var survivor = b.PrimaryVisual;
            var disposed = 0;
            a.OnDispose(() => { a.Dispose(); ctx.RemoveShape(primary); throw new Exception("cleanup failure"); });
            a.AddResource(new Resource(() => disposed++));
            Assert.Throws<AggregateException>(() => ctx.RemoveShape(label));
            a.Dispose();
            Assert.Equal(1, disposed);
            Assert.Contains(survivor, viewer.WindowForTests.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
            Assert.Equal(2, viewer.WindowForTests.MeasurementOverlay.Canvas.Children.Count);
        });
    }

    [Fact]
    public async Task RegistrationHandlesReentrantRemovalDuringAttachment()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var ctx = viewer.MeasurementContext; var overlay = viewer.WindowForTests.MeasurementOverlay;
            void Added(UIElement element) => ctx.RemoveShape(element);
            overlay.VisualAdded += Added;
            IMeasurement item;
            try { item = ctx.CreateMeasurement(MeasurementGeometry.Point(new())); }
            finally { overlay.VisualAdded -= Added; }
            Assert.True(item.IsDisposed);
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Fact]
    public async Task ClearFinishesAllMeasurementsEvenWhenCancelAndCleanupThrow()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var completed = viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Point(new())); var disposed = 0;
            completed.Complete();
            completed.OnDispose(() => throw new Exception("cleanup failed"));
            completed.AddResource(new Resource(() => disposed++));
            completed.OnDispose(() => Assert.Throws<InvalidOperationException>(() => viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Point(new()))));
            var tool = new Tool(); viewer.RegisterMeasurementTool(tool);
            viewer.StartMeasurement(tool.Id); viewer.Interaction.ImageDown(1, 2);
            Assert.Throws<InvalidOperationException>(() => viewer.ClearShapes());
            Assert.Equal(1, tool.Disposals); Assert.Equal(1, disposed);
            Assert.Empty(viewer.WindowForTests.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("unregister")]
    public async Task EndingToolReleasesOnlyPreview(string action)
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var tool = new Tool { Finish = true }; viewer.RegisterMeasurementTool(tool);
            viewer.StartMeasurement(tool.Id); viewer.Interaction.ImageDown(1, 2);
            tool.Finish = false; viewer.StartMeasurement(tool.Id); viewer.Interaction.ImageDown(3, 4);
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (action == "hide") viewer.Layers.Measurements.IsVisible = false;
                else viewer.UnregisterMeasurementTool(tool.Id);
            });
            Assert.Equal(1, tool.Disposals);
            Assert.Equal(2, viewer.WindowForTests.MeasurementOverlay.Canvas.Children.Count);
        });
    }

    [Fact]
    public async Task ClosingViewerReleasesCompletedResources()
    {
        var viewer = Create(); var disposed = 0;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var scope = viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Point(new()));
            scope.AddResource(new Resource(() => disposed++)); scope.Complete();
        });
        await viewer.DisposeAsync(); await viewer.DisposeAsync();
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task EditorFactoryRejectsUnsupportedShapesAndRetainsOppositeCorner()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Rectangle(new(2, 2), new(6, 6)));
            var rectangle = (System.Windows.Shapes.Rectangle)item.PrimaryVisual;
            using var session = ShapeEditorFactory.Create(rectangle, item);
            Assert.NotNull(session); session.BeginDrag(0); session.Update(new(8, 9)); session.Update(new(10, 11));
            Assert.Equal(4, rectangle.Width); Assert.Equal(5, rectangle.Height);
            Assert.Equal(6, System.Windows.Controls.Canvas.GetLeft(rectangle));
            Assert.Null(ShapeEditorFactory.Create(new System.Windows.Controls.Border(), null));
        });
    }

    [Fact]
    public async Task DisposedCoordinatorLeavesNoStandaloneInteraction()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.WindowForTests.MeasurementOverlay;
            var shape = Shapes.CreatePoint(new()); viewer.MeasurementContext.AttachVisualInternal(shape);
            viewer.Interaction.Dispose();
            viewer.Interaction.Select(shape); viewer.Interaction.StartEditing(shape); viewer.Interaction.DeleteSelected();
            Assert.Null(viewer.Interaction.SelectedShape); Assert.False(viewer.Interaction.Editor.IsEditing);
            Assert.Single(overlay.Canvas.Children.Cast<UIElement>());
        });
    }
}



