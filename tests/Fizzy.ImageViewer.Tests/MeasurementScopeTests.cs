using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementScopeTests
{
    private static Viewer Create() => new(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
    private sealed class Resource(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    private sealed class Tool : IMeasureMethod
    {
        public string Id => "custom";
        public string DisplayName => "Custom";
        public int Disposals;
        public bool Finish;
        public bool OnClick(Point point, IMeasureToolContext context)
        {
            var scope = context.CreateScope();
            scope.AddShape(Shapes.CreatePoint(point));
            scope.AddResource(new Resource(() => Disposals++));
            if (Finish) scope.Complete();
            return Finish;
        }
        public void OnMouseMove(Point point, IMeasureToolContext context) { }
        // Intentionally does not release anything: the manager must provide the guarantee.
        public void Cancel(IMeasureToolContext context) => throw new InvalidOperationException("Plugin cancel failed");
    }

    [Fact]
    public async Task CancelReleasesPreviewEvenWhenToolThrowsButRetainsCompletedResult()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var tool = new Tool { Finish = true };
            viewer.RegisterMeasureMethod(tool);
            viewer.StartMeasure(tool.Id); viewer.Interaction.ImageDown(2, 3);
            tool.Finish = false;
            viewer.StartMeasure(tool.Id); viewer.Interaction.ImageDown(4, 5);
            Assert.Throws<InvalidOperationException>(() => viewer.CancelMeasure());
            Assert.Equal(1, tool.Disposals);
            Assert.Single(viewer.WindowForTests.Layer1.Canvas.Children.Cast<UIElement>());
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
            Assert.Same(survivor, Assert.Single(viewer.WindowForTests.Layer1.Canvas.Children.Cast<UIElement>()));
        });
    }

    [Fact]
    public async Task RegistrationHandlesReentrantDisposalAndRejectsSharedVisuals()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var ctx = viewer.MeasurementContext; var overlay = viewer.WindowForTests.Layer1;
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
            var tool = new Tool(); viewer.RegisterMeasureMethod(tool);
            viewer.StartMeasure(tool.Id); viewer.Interaction.ImageDown(1, 2);
            Assert.Throws<InvalidOperationException>(() => viewer.ClearShapes());
            Assert.Equal(1, tool.Disposals); Assert.Equal(1, disposed);
            Assert.Empty(viewer.WindowForTests.Layer1.Canvas.Children.Cast<UIElement>());
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
            var tool = new Tool { Finish = true }; viewer.RegisterMeasureMethod(tool);
            viewer.StartMeasure(tool.Id); viewer.Interaction.ImageDown(1, 2);
            tool.Finish = false; viewer.StartMeasure(tool.Id); viewer.Interaction.ImageDown(3, 4);
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (action == "hide") viewer.Layers.Measurements.IsVisible = false;
                else viewer.UnregisterMeasureMethod(tool.Id);
            });
            Assert.Equal(1, tool.Disposals);
            Assert.Single(viewer.WindowForTests.Layer1.Canvas.Children.Cast<UIElement>());
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
    public async Task RegistrySupportsRemovalAndRectangleDragKeepsOriginalOppositeCorner()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var registry = ShapeEditorRegistry.CreateDefault();
            var rectangle = Shapes.CreateRectangle(); rectangle.Width = 4; rectangle.Height = 4;
            System.Windows.Controls.Canvas.SetLeft(rectangle, 2); System.Windows.Controls.Canvas.SetTop(rectangle, 2);
            using var session = registry.Create(rectangle, null);
            Assert.NotNull(session); session.BeginDrag(0); session.UpdateDrag(new(8, 9)); session.UpdateDrag(new(10, 11));
            Assert.Equal(4, rectangle.Width); Assert.Equal(5, rectangle.Height);
            Assert.Equal(6, System.Windows.Controls.Canvas.GetLeft(rectangle));
            Assert.True(registry.Unregister(ShapeType.Rectangle));
            Assert.Null(registry.Create(rectangle, null));
        });
    }

    [Fact]
    public async Task DisposedCoordinatorLeavesNoStandaloneInteraction()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.WindowForTests.Layer1;
            var shape = Shapes.CreatePoint(new()); viewer.MeasurementContext.AttachVisualInternal(shape);
            viewer.Interaction.Dispose();
            viewer.Interaction.Select(shape); viewer.Interaction.StartEditing(shape); viewer.Interaction.DeleteSelected();
            Assert.Null(viewer.Interaction.SelectedShape); Assert.False(viewer.Interaction.Editor.IsEditing);
            Assert.Single(overlay.Canvas.Children.Cast<UIElement>());
        });
    }
}



