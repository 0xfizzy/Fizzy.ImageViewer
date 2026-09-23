using Fizzy.ImageViewer.Measurements.Presentation;
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            owner = viewer.Host.Measurements.CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
            var item = (MeasurementItem)owner;
            viewer.Host.Window.MeasurementOverlay.UpdateScale(2);
            owner.UpdateGeometry(MeasurementGeometry.Point(new(3, 4)));
            Assert.Equal(5.5, System.Windows.Controls.Canvas.GetLeft(item.Label));
            Assert.Equal(new Point(3, 4), item.Geometry.Start);
            Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Point(new(double.NaN, 1)));
        });
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() => owner!.UpdateGeometry(MeasurementGeometry.Point(new()))));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            owner!.Dispose();
            Assert.Throws<ObjectDisposedException>(() => owner.UpdateGeometry(MeasurementGeometry.Point(new())));
        });
    }

    [Theory]
    [InlineData(MeasurementKind.Point)]
    [InlineData(MeasurementKind.Crosshair)]
    public async Task AnchorEditorUpdatesModelAndRetainsVisualGeometry(MeasurementKind kind)
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)viewer.Host.Measurements.CreateMeasurement(kind == MeasurementKind.Point
                ? MeasurementGeometry.Point(new(1, 2)) : MeasurementGeometry.Crosshair(new(1, 2)));
            var shape = (System.Windows.Shapes.Path)item.PrimaryVisual;
            var geometry = shape.Data;
            using var target = new MeasurementEditSession(item);
            target.BeginDrag(0); target.Update(new(5, 6)); target.EndDrag();
            viewer.Host.Window.MeasurementOverlay.UpdateScale(2);
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var tool = new Tool { Finish = true };
            viewer.RegisterMeasurementTool(tool);
            viewer.StartMeasurement(tool.Id); viewer.Host.Interaction.ImageDown(2, 3);
            tool.Finish = false;
            viewer.StartMeasurement(tool.Id); viewer.Host.Interaction.ImageDown(4, 5);
            Assert.Throws<InvalidOperationException>(() => viewer.CancelMeasurement());
            Assert.Equal(1, tool.Disposals);
            Assert.Equal(2, viewer.Host.Window.MeasurementOverlay.Canvas.Children.Count);
            viewer.ClearShapes();
            Assert.Equal(2, tool.Disposals);
        });
    }

    [Fact]
    public async Task RemovingMeasurementCleansOnlyItsVisualsDespiteFailuresAndReentry()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var ctx = viewer.Host.Measurements;
            var a = (MeasurementItem)ctx.CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
            var b = (MeasurementItem)ctx.CreateMeasurement(MeasurementGeometry.Point(new(3, 4)));
            var primary = a.PrimaryVisual; var label = a.Label; a.Complete(); b.Complete();
            var survivor = b.PrimaryVisual;
            var disposed = 0;
            a.OnDispose(() => { a.Dispose(); ctx.Find(primary)?.Dispose(); throw new Exception("cleanup failure"); });
            a.AddResource(new Resource(() => disposed++));
            Assert.Throws<AggregateException>(() => ctx.Find(label)?.Dispose());
            a.Dispose();
            Assert.Equal(1, disposed);
            Assert.Contains(survivor, viewer.Host.Window.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
            Assert.Equal(2, viewer.Host.Window.MeasurementOverlay.Canvas.Children.Count);
        });
    }

    [Fact]
    public async Task RegistrationHandlesReentrantRemovalDuringAttachment()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var ctx = viewer.Host.Measurements; var overlay = viewer.Host.Window.MeasurementOverlay;
            void Added(UIElement element) => ctx.Find(element)?.Dispose();
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var completed = viewer.Host.Measurements.CreateMeasurement(MeasurementGeometry.Point(new())); var disposed = 0;
            completed.Complete();
            completed.OnDispose(() => throw new Exception("cleanup failed"));
            completed.AddResource(new Resource(() => disposed++));
            completed.OnDispose(() => Assert.Throws<InvalidOperationException>(() => viewer.Host.Measurements.CreateMeasurement(MeasurementGeometry.Point(new()))));
            var tool = new Tool(); viewer.RegisterMeasurementTool(tool);
            viewer.StartMeasurement(tool.Id); viewer.Host.Interaction.ImageDown(1, 2);
            Assert.Throws<InvalidOperationException>(() => viewer.ClearShapes());
            Assert.Equal(1, tool.Disposals); Assert.Equal(1, disposed);
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("unregister")]
    public async Task EndingToolReleasesOnlyPreview(string action)
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var tool = new Tool { Finish = true }; viewer.RegisterMeasurementTool(tool);
            viewer.StartMeasurement(tool.Id); viewer.Host.Interaction.ImageDown(1, 2);
            tool.Finish = false; viewer.StartMeasurement(tool.Id); viewer.Host.Interaction.ImageDown(3, 4);
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (action == "hide") viewer.Layers.Measurements.IsVisible = false;
                else viewer.UnregisterMeasurementTool(tool.Id);
            });
            Assert.Equal(1, tool.Disposals);
            Assert.Equal(2, viewer.Host.Window.MeasurementOverlay.Canvas.Children.Count);
        });
    }

    [Fact]
    public async Task ClosingViewerReleasesCompletedResources()
    {
        var viewer = Create(); var disposed = 0;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var scope = viewer.Host.Measurements.CreateMeasurement(MeasurementGeometry.Point(new()));
            scope.AddResource(new Resource(() => disposed++)); scope.Complete();
        });
        await viewer.DisposeAsync(); await viewer.DisposeAsync();
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task EditingRejectsUnsupportedShapesAndRetainsOppositeCorner()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)viewer.Host.Measurements.CreateMeasurement(MeasurementGeometry.Rectangle(new(2, 2), new(6, 6)));
            var rectangle = (System.Windows.Shapes.Rectangle)item.PrimaryVisual;
            using var session = new MeasurementEditSession(item);
            Assert.NotNull(session); session.BeginDrag(0); session.Update(new(8, 9)); session.Update(new(10, 11));
            Assert.Equal(4, rectangle.Width); Assert.Equal(5, rectangle.Height);
            Assert.Equal(6, System.Windows.Controls.Canvas.GetLeft(rectangle));
            Assert.False(viewer.Host.Interaction.Editor.CanEdit(null));
        });
    }

    [Fact]
    public async Task DisposedCoordinatorLeavesNoStandaloneInteraction()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Host.Window.MeasurementOverlay;
            var shape = MeasurementVisualFactory.CreatePoint(new()); viewer.Host.Window.MeasurementOverlay.AddShape(shape);
            viewer.Host.Interaction.Dispose();
            viewer.Host.Interaction.Select(viewer.Host.Measurements.Find(shape)); viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(shape)); viewer.Host.Interaction.DeleteSelected();
            Assert.Null(viewer.Host.Interaction.SelectedMeasurement); Assert.False(viewer.Host.Interaction.Editor.IsEditing);
            Assert.Single(overlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Fact]
    public async Task UnregisteredVisualCannotReplaceOrDeleteSelectedMeasurement()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var context = viewer.Host.Measurements;
            var interaction = viewer.Host.Interaction;
            var item = (MeasurementItem)context.CreateMeasurement(MeasurementGeometry.Point(new(2, 3)));
            item.Complete();
            var orphan = MeasurementVisualFactory.CreatePoint(new(5, 6));
            viewer.Host.Window.MeasurementOverlay.AddShape(orphan);
            interaction.Select(item);
            Assert.False(interaction.Hit(orphan));
            Assert.Same(item, interaction.SelectedMeasurement);
            interaction.StartEditing(context.Find(orphan));
            Assert.False(interaction.Editor.IsEditing);
            interaction.Delete(context.Find(orphan));
            Assert.Same(item, interaction.SelectedMeasurement);
            Assert.False(item.IsDisposed);
            interaction.DeleteSelected();
            Assert.True(item.IsDisposed);
            Assert.Contains(orphan, viewer.Host.Window.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
            Assert.False(interaction.Hit(orphan));
            Assert.Null(interaction.SelectedMeasurement);
        });
    }
}



