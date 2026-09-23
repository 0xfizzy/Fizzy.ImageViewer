using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Input;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementReentryTests
{
    private sealed class Tool(string id) : IMeasurementTool
    {
        public string Id => id;
        public string DisplayName => id;
        public Func<IMeasurementToolContext, bool>? Click;
        public Action<IMeasurementToolContext>? Move, Cancelled;
        public int Clicks;
        public bool OnClick(Point point, IMeasurementToolContext context) { Clicks++; return Click?.Invoke(context) ?? false; }
        public void OnMouseMove(Point point, IMeasurementToolContext context) => Move?.Invoke(context);
        public void Cancel(IMeasurementToolContext context) => Cancelled?.Invoke(context);
    }

    private sealed class Harness : IDisposable
    {
        internal readonly ImageLayer Input = new();
        internal readonly OverlayLayer Overlay = new();
        internal readonly ViewerLayers Layers;
        internal readonly PixelQueryScheduler Queries;
        internal readonly MeasurementToolRegistry Tools;
        internal readonly MeasurementContext Context;
        internal readonly InteractionCoordinator Coordinator;
        internal Harness()
        {
            Layers = new(Input.TransformGroup);
            Layers.Measurements.Root.Children.Add(Overlay);
            Queries = new(() => null, NullLogger.Instance, new DispatcherQueryRuntime(Overlay.Dispatcher));
            Context = new(Overlay, () => null, Queries, NullLogger.Instance);
            Tools = new();
            Coordinator = new(new ViewerInputBinding(Input, Overlay), Overlay, new EditManager(Overlay, Context.Find), Tools, Context, Layers);
        }
        internal void AssertActive(string id)
        {
            Assert.Equal(id, Coordinator.ActiveId);
            Assert.Equal(InteractionMode.Measuring, Coordinator.Mode);
            Assert.True(Layers.InputSuppressed);
            Assert.Same(Cursors.Pen, Input.Container.Cursor);
        }
        public void Dispose() { try { Coordinator.Dispose(); } finally { Context.Shutdown(); Queries.Dispose(); } }
    }

    [Theory]
    [InlineData("click", false)]
    [InlineData("click", true)]
    [InlineData("move", false)]
    [InlineData("move", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("start", false)]
    [InlineData("start", true)]
    [InlineData("edit", false)]
    [InlineData("edit", true)]
    public async Task ReentrantSessionOwnsInputAndPreview(string operation, bool throws)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            using var h = new Harness();
            var old = new Tool("old"); var next = new Tool("next"); var outer = new Tool("outer");
            h.Tools.RegisterTool(old); h.Tools.RegisterTool(next); h.Tools.RegisterTool(outer);
            var oldReleased = 0; var nextReleased = 0;
            next.Click = ctx => { var scope = ctx.CreateMeasurement(MeasurementGeometry.Point(new()));  scope.OnDispose(() => nextReleased++); return false; };
            var failure = new InvalidOperationException("old callback");
            void Restart(IMeasurementToolContext _)
            {
                h.Coordinator.StartMeasurement(next.Id);
                h.Coordinator.ImageDown(1, 1);
                if (throws) throw failure;
            }
            h.Coordinator.StartMeasurement(old.Id);
            var scope = h.Context.CreateMeasurement(MeasurementGeometry.Point(new())); scope.OnDispose(() => oldReleased++);
            var editable = (MeasurementItem)h.Context.CreateMeasurement(MeasurementGeometry.Point(new())); editable.Complete();
            var shape = editable.PrimaryVisual;
            Action invoke;
            switch (operation)
            {
                case "click": old.Click = ctx => { Restart(ctx); return true; }; invoke = () => h.Coordinator.ImageDown(0, 0); break;
                case "move": old.Move = Restart; invoke = () => h.Coordinator.ImageMove(0, 0); break;
                case "start": old.Cancelled = Restart; invoke = () => h.Coordinator.StartMeasurement(outer.Id); break;
                case "edit": old.Cancelled = Restart; invoke = () => h.Coordinator.StartEditing(shape); break;
                default: old.Cancelled = Restart; invoke = h.Coordinator.Cancel; break;
            }
            if (throws) Assert.Same(failure, Assert.Throws<InvalidOperationException>(invoke)); else invoke();
            h.AssertActive(next.Id);
            Assert.Equal(1, oldReleased); Assert.Equal(0, nextReleased);
            next.Click = null;
            h.Coordinator.ImageDown(2, 2); Assert.Equal(2, next.Clicks);
            h.Coordinator.Cancel(); Assert.Equal(1, nextReleased);
            Assert.Equal(InteractionMode.Idle, h.Coordinator.Mode); Assert.False(h.Layers.InputSuppressed);
        });
    }

    [Theory]
    [InlineData("click")]
    [InlineData("move")]
    [InlineData("start")]
    public async Task FailureWithoutReentryRestoresIdle(string operation)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            using var h = new Harness(); var tool = new Tool("old"); h.Tools.RegisterTool(tool);
            h.Coordinator.StartMeasurement(tool.Id);
            var released = 0; h.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => released++);
            var failure = new InvalidOperationException("failure");
            Action invoke;
            if (operation == "click") { tool.Click = _ => throw failure; invoke = () => h.Coordinator.ImageDown(0, 0); }
            else if (operation == "move") { tool.Move = _ => throw failure; invoke = () => h.Coordinator.ImageMove(0, 0); }
            else { tool.Cancelled = _ => throw failure; invoke = () => h.Coordinator.StartMeasurement(tool.Id); }
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(invoke));
            Assert.Null(h.Coordinator.ActiveId); Assert.Equal(InteractionMode.Idle, h.Coordinator.Mode);
            Assert.False(h.Layers.InputSuppressed); Assert.Same(Cursors.Cross, h.Input.Container.Cursor); Assert.Equal(1, released);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeCleanupCanStartSessionAndCreatePreview(bool completes)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            using var h = new Harness(); var tool = new Tool("same"); h.Tools.RegisterTool(tool);
            h.Coordinator.StartMeasurement(tool.Id);
            var released = 0;
            var old = h.Context.CreateMeasurement(MeasurementGeometry.Point(new()));
            old.OnDispose(() =>
            {
                h.Coordinator.StartMeasurement(tool.Id);
                h.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => released++);
            });
            tool.Click = _ => true;
            if (completes) h.Coordinator.ImageDown(0, 0); else h.Coordinator.Cancel();
            h.AssertActive(tool.Id); Assert.Equal(0, released);
            h.Coordinator.Cancel(); Assert.Equal(1, released);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingCoordinatorRejectsReentryAndStillCleans(bool throws)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            using var h = new Harness(); var tool = new Tool("old"); var next = new Tool("next");
            h.Tools.RegisterTool(tool); h.Tools.RegisterTool(next);
            h.Coordinator.StartMeasurement(tool.Id);
            var released = 0; h.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => released++);
            tool.Cancelled = _ => { h.Coordinator.StartMeasurement(next.Id); if (throws) throw new InvalidOperationException(); };
            if (throws) Assert.Throws<InvalidOperationException>(h.Coordinator.Dispose); else h.Coordinator.Dispose();
            h.Coordinator.Dispose();
            Assert.Null(h.Coordinator.ActiveId); Assert.Equal(InteractionMode.Idle, h.Coordinator.Mode);
            Assert.False(h.Layers.InputSuppressed); Assert.Equal(1, released);
        });
    }

    [Fact]
    public async Task ClosingViewerRejectsPublicStartAndReleasesAllScopes()
    {
        var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        var rejected = false; var released = 0;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var tool = new Tool("closing");
            tool.Cancelled = _ => { Assert.Throws<ObjectDisposedException>(() => viewer.StartMeasurement(tool.Id)); rejected = true; throw new InvalidOperationException(); };
            viewer.RegisterMeasurementTool(tool); viewer.StartMeasurement(tool.Id);
            var completed = viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Point(new())); completed.OnDispose(() => released++); completed.Complete();
            viewer.MeasurementContext.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => released++);
        });
        await viewer.DisposeAsync(); await viewer.DisposeAsync();
        Assert.True(rejected); Assert.Equal(2, released);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnregisterPreservesReplacementWithSameId(bool throws)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            using var h = new Harness();
            var old = new Tool("same");
            var replacement = new Tool("same");
            h.Tools.RegisterTool(old);
            h.Coordinator.StartMeasurement(old.Id);
            var oldReleased = 0; var replacementReleased = 0;
            h.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => oldReleased++);
            var failure = new InvalidOperationException("outgoing cancellation failed");
            old.Cancelled = _ =>
            {
                h.Tools.RegisterTool(replacement);
                h.Coordinator.StartMeasurement(replacement.Id);
                h.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => replacementReleased++);
                if (throws) throw failure;
            };
            if (throws)
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => h.Coordinator.UnregisterMeasurementTool(old.Id)));
            else Assert.True(h.Coordinator.UnregisterMeasurementTool(old.Id));
            h.AssertActive(replacement.Id);
            h.Coordinator.ImageDown(1, 1);
            Assert.Equal(0, old.Clicks);
            Assert.Equal(1, replacement.Clicks);
            Assert.Equal(1, oldReleased);
            Assert.Equal(0, replacementReleased);
            h.Coordinator.Cancel();
            Assert.Equal(1, replacementReleased);
        });
    }
}
