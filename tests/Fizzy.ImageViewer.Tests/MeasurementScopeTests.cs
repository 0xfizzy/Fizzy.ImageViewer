using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Editing;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementScopeTests
{
    [Fact]
    public async Task ScopeMovesOnlyOwnedAnchorsAndRetainsZoomPolicy()
    {
        await using var viewer = Create();
        IMeasurementScope? owner = null;
        System.Windows.Controls.TextBlock? label = null;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            owner = viewer.MeasurementContext.CreateScope();
            label = Shapes.CreateLabel(new(1, 2), "preview", 8, 12);
            owner.AddShape(label);
            var overlay = viewer.WindowForTests.MeasurementOverlay;
            overlay.UpdateScale(2);
            owner.UpdateAnchor(label, new(3, 4));
            Assert.Equal(7, System.Windows.Controls.Canvas.GetLeft(label));
            Assert.Equal(10, System.Windows.Controls.Canvas.GetTop(label));
            overlay.UpdateScale(4);
            Assert.Equal(5, System.Windows.Controls.Canvas.GetLeft(label));
            Assert.Equal(7, System.Windows.Controls.Canvas.GetTop(label));
            var other = viewer.MeasurementContext.CreateScope();
            Assert.Throws<ArgumentException>(() => other.UpdateAnchor(label, new()));
            Assert.Throws<ArgumentOutOfRangeException>(() => owner.UpdateAnchor(label, new(double.NaN, 1)));
            Assert.Equal(5, System.Windows.Controls.Canvas.GetLeft(label));
        });
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() => owner!.UpdateAnchor(label!, new())));
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            owner!.Dispose();
            Assert.Throws<ObjectDisposedException>(() => owner.UpdateAnchor(label!, new()));
        });
    }

    [Theory]
    [InlineData(ShapeType.Point)]
    [InlineData(ShapeType.Crosshair)]
    public async Task AnchorEditorMovesSupportedShapesWithoutChangingTheirGeometry(ShapeType kind)
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var shape = kind == ShapeType.Point ? Shapes.CreatePoint(new(1, 2)) : Shapes.CreateCrosshair(new(1, 2));
            var geometry = shape.Data;
            viewer.MeasurementContext.CreateScope().AddShape(shape);
            using var session = ShapeEditorFactory.Create(shape, null);
            Assert.NotNull(session);
            session.BeginDrag(0); session.UpdateDrag(new(5, 6)); session.EndDrag();
            viewer.WindowForTests.MeasurementOverlay.UpdateScale(2);
            Assert.Equal(new Point(5, 6), Assert.Single(session.Points));
            Assert.Equal(5, System.Windows.Controls.Canvas.GetLeft(shape));
            Assert.Equal(6, System.Windows.Controls.Canvas.GetTop(shape));
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
            var scope = context.CreateScope();
            scope.AddShape(Shapes.CreatePoint(point));
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
            Assert.Single(viewer.WindowForTests.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
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
            var a = ctx.CreateScope(); var b = ctx.CreateScope();
            var primary = Shapes.CreatePoint(new(1, 2)); var label = Shapes.CreateLabel(new(), "custom");
            a.AddShape(primary); a.AddShape(label); a.Complete();
            var survivor = Shapes.CreatePoint(new(3, 4)); b.AddShape(survivor); b.Complete();
            var disposed = 0;
            a.OnDispose(() => { a.Dispose(); ctx.RemoveShape(primary); throw new Exception("cleanup failure"); });
            a.AddResource(new Resource(() => disposed++));
            Assert.Throws<AggregateException>(() => ctx.RemoveShape(label));
            a.Dispose();
            Assert.Equal(1, disposed);
            Assert.Same(survivor, Assert.Single(viewer.WindowForTests.MeasurementOverlay.Canvas.Children.Cast<UIElement>()));
        });
    }

    [Fact]
    public async Task RegistrationHandlesReentrantDisposalAndRejectsSharedVisuals()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var ctx = viewer.MeasurementContext; var overlay = viewer.WindowForTests.MeasurementOverlay;
            var a = ctx.CreateScope(); var b = ctx.CreateScope(); var shape = Shapes.CreatePoint(new());
            a.AddShape(shape);
            Assert.Throws<ArgumentException>(() => b.AddShape(shape));
            b.Dispose(); Assert.Single(overlay.Canvas.Children.Cast<UIElement>());
            a.Dispose();
            var c = ctx.CreateScope();
            void Added(UIElement element) => c.Dispose();
            overlay.VisualAdded += Added;
            try { c.AddShape(Shapes.CreatePoint(new())); } finally { overlay.VisualAdded -= Added; }
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Fact]
    public async Task ClearFinishesAllScopesEvenWhenCancelAndCleanupThrow()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var completed = viewer.MeasurementContext.CreateScope(); var disposed = 0;
            completed.AddShape(Shapes.CreatePoint(new())); completed.Complete();
            completed.OnDispose(() => throw new Exception("cleanup failed"));
            completed.AddResource(new Resource(() => disposed++));
            completed.OnDispose(() => Assert.Throws<InvalidOperationException>(() => viewer.MeasurementContext.CreateScope()));
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
            Assert.Single(viewer.WindowForTests.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Fact]
    public async Task ClosingViewerReleasesCompletedResources()
    {
        var viewer = Create(); var disposed = 0;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var scope = viewer.MeasurementContext.CreateScope();
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
            var rectangle = Shapes.CreateRectangle(); rectangle.Width = 4; rectangle.Height = 4;
            System.Windows.Controls.Canvas.SetLeft(rectangle, 2); System.Windows.Controls.Canvas.SetTop(rectangle, 2);
            using var session = ShapeEditorFactory.Create(rectangle, null);
            Assert.NotNull(session); session.BeginDrag(0); session.UpdateDrag(new(8, 9)); session.UpdateDrag(new(10, 11));
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



