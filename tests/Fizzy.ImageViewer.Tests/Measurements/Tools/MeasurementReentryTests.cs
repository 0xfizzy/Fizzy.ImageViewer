using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Measurements.Editing;
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
        public IMeasurementToolContext Context = null!;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        {
            Context = context;
            return new TestMeasurementSession(point => OnClick(point, context),
                point => OnMouseMove(point, context), () => Cancel(context));
        }
        public bool OnClick(Point point, IMeasurementToolContext context) { Clicks++; return Click?.Invoke(context) ?? false; }
        public void OnMouseMove(Point point, IMeasurementToolContext context) => Move?.Invoke(context);
        public void Cancel(IMeasurementToolContext context) => Cancelled?.Invoke(context);
    }

    private sealed class Harness : IDisposable
    {
        internal readonly ImageViewport Input = new();
        internal readonly MeasurementOverlay Overlay;
        internal readonly ViewerLayers Layers;
        internal readonly PixelQueryScheduler Queries;
        internal readonly MeasurementToolRegistry Tools;
        internal readonly MeasurementCollection Context;
        internal readonly MeasurementRuntime Runtime;
        internal readonly MeasurementInteractionCoordinator Coordinator;
        internal Harness()
        {
            Layers = new(Input.TransformGroup);
            Overlay = Layers.Measurements.Overlay;
            Queries = new(() => null, NullLogger.Instance, new DispatcherQueryRuntime(Overlay.Dispatcher));
            Runtime = new(new ViewerLifetime(), Overlay.Dispatcher, Queries, NullLogger.Instance);
            Context = new(Layers.Measurements, Runtime, NullLogger.Instance);
            Tools = new();
            var binding = new ViewerInputBinding(Input, Overlay, Context, Layers.Collection);
            Coordinator = new(binding, new MeasurementEditController(Overlay), Tools, Context, Layers.Measurements, Runtime, () => null);
            binding.Connect(Coordinator);
        }
        internal void AssertActive(string id)
        {
            Assert.Equal(id, Coordinator.ActiveId);
            Assert.Equal(InteractionMode.Measuring, Coordinator.Mode);
            Assert.True(Layers.Collection.InputSuppressed);
            Assert.Same(Cursors.Pen, Input.Container.Cursor);
        }
        public void Dispose() { try { Coordinator.Dispose(); } finally { Context.Shutdown(); Queries.Dispose(); } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionOwnsOnlyItsPreviewsAndExpiredContextsCannotCreate(bool complete)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var h = new Harness();
            var independent = new MeasurementCreationContext(h.Context, h.Runtime, () => null).CreateMeasurement(MeasurementGeometry.Point(new()));
            var tool = new Tool("scope");
            IMeasurementToolContext? retainedContext = null;
            IMeasurement? preview = null;
            tool.Click = context =>
            {
                retainedContext = context;
                preview = context.CreateMeasurement(MeasurementGeometry.Point(new(2, 3)));
                return complete;
            };
            h.Tools.RegisterTool(tool);
            h.Coordinator.ActivateMeasurementTool(tool.Id);
            h.Coordinator.ImageDown(2, 3);
            if (!complete) h.Coordinator.Cancel();
            Assert.True(preview!.IsDisposed);
            Assert.False(independent.IsDisposed);
            h.Coordinator.ActivateMeasurementTool(tool.Id);
            Assert.Throws<ObjectDisposedException>(() => retainedContext!.CreateMeasurement(MeasurementGeometry.Point(new())));
            h.Context.ClearMeasurements();
            Assert.True(independent.IsDisposed);
        });
    }

    [Fact]
    public async Task InterruptedCallbackCannotAttachNewPreviewToItsReplacementSession()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var h = new Harness();
            var old = new Tool("old");
            var next = new Tool("next");
            IMeasurement? survivor = null;
            next.Click = context =>
            {
                survivor = context.CreateMeasurement(MeasurementGeometry.Point(new(1, 1)));
                return false;
            };
            old.Click = context =>
            {
                context.CreateMeasurement(MeasurementGeometry.Point(new()));
                h.Coordinator.ActivateMeasurementTool(next.Id);
                h.Coordinator.ImageDown(1, 1);
                Assert.Throws<ObjectDisposedException>(() => context.CreateMeasurement(MeasurementGeometry.Point(new())));
                return true;
            };
            h.Tools.RegisterTool(old);
            h.Tools.RegisterTool(next);
            h.Coordinator.ActivateMeasurementTool(old.Id);
            h.Coordinator.ImageDown(0, 0);
            h.AssertActive(next.Id);
            Assert.False(survivor!.IsDisposed);
            h.Coordinator.Cancel();
            Assert.True(survivor.IsDisposed);
            Assert.Empty(h.Overlay.Canvas.Children);
        });
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var h = new Harness();
            var old = new Tool("old"); var next = new Tool("next"); var outer = new Tool("outer");
            h.Tools.RegisterTool(old); h.Tools.RegisterTool(next); h.Tools.RegisterTool(outer);
            var oldReleased = 0; var nextReleased = 0;
            next.Click = ctx => { var scope = ctx.CreateMeasurement(MeasurementGeometry.Point(new()));  scope.OnDispose(() => nextReleased++); return false; };
            var failure = new InvalidOperationException("old callback");
            void Restart(IMeasurementToolContext _)
            {
                h.Coordinator.ActivateMeasurementTool(next.Id);
                h.Coordinator.ImageDown(1, 1);
                if (throws) throw failure;
            }
            h.Coordinator.ActivateMeasurementTool(old.Id);
            var scope = old.Context.CreateMeasurement(MeasurementGeometry.Point(new())); scope.OnDispose(() => oldReleased++);
            var editable = (MeasurementItem)new MeasurementCreationContext(h.Context, h.Runtime, () => null).CreateMeasurement(MeasurementGeometry.Point(new())); editable.Complete();
            var shape = editable.Presentation.PrimaryVisual;
            Action invoke;
            switch (operation)
            {
                case "click": old.Click = ctx => { Restart(ctx); return true; }; invoke = () => h.Coordinator.ImageDown(0, 0); break;
                case "move": old.Move = Restart; invoke = () => h.Coordinator.ImageMove(0, 0); break;
                case "start": old.Cancelled = Restart; invoke = () => h.Coordinator.ActivateMeasurementTool(outer.Id); break;
                case "edit": old.Cancelled = Restart; invoke = () => h.Coordinator.StartEditing(editable); break;
                default: old.Cancelled = Restart; invoke = h.Coordinator.Cancel; break;
            }
            if (throws) Assert.Same(failure, Assert.Throws<InvalidOperationException>(invoke)); else invoke();
            h.AssertActive(next.Id);
            Assert.Equal(1, oldReleased); Assert.Equal(0, nextReleased);
            next.Click = null;
            h.Coordinator.ImageDown(2, 2); Assert.Equal(2, next.Clicks);
            h.Coordinator.Cancel(); Assert.Equal(1, nextReleased);
            Assert.Equal(InteractionMode.Idle, h.Coordinator.Mode); Assert.False(h.Layers.Collection.InputSuppressed);
        });
    }

    [Theory]
    [InlineData("click")]
    [InlineData("move")]
    [InlineData("start")]
    public async Task FailureWithoutReentryRestoresIdle(string operation)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var h = new Harness(); var tool = new Tool("old"); h.Tools.RegisterTool(tool);
            h.Coordinator.ActivateMeasurementTool(tool.Id);
            var released = 0; tool.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => released++);
            var failure = new InvalidOperationException("failure");
            Action invoke;
            if (operation == "click") { tool.Click = _ => throw failure; invoke = () => h.Coordinator.ImageDown(0, 0); }
            else if (operation == "move") { tool.Move = _ => throw failure; invoke = () => h.Coordinator.ImageMove(0, 0); }
            else { tool.Cancelled = _ => throw failure; invoke = () => h.Coordinator.ActivateMeasurementTool(tool.Id); }
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(invoke));
            Assert.Null(h.Coordinator.ActiveId); Assert.Equal(InteractionMode.Idle, h.Coordinator.Mode);
            Assert.False(h.Layers.Collection.InputSuppressed); Assert.Same(Cursors.Cross, h.Input.Container.Cursor); Assert.Equal(1, released);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeCleanupCanStartSessionAndCreatePreview(bool completes)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var h = new Harness(); var tool = new Tool("same"); h.Tools.RegisterTool(tool);
            h.Coordinator.ActivateMeasurementTool(tool.Id);
            var released = 0;
            var old = tool.Context.CreateMeasurement(MeasurementGeometry.Point(new()));
            old.OnDispose(() =>
            {
                h.Coordinator.ActivateMeasurementTool(tool.Id);
                tool.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => released++);
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var h = new Harness(); var tool = new Tool("old"); var next = new Tool("next");
            h.Tools.RegisterTool(tool); h.Tools.RegisterTool(next);
            h.Coordinator.ActivateMeasurementTool(tool.Id);
            var released = 0; tool.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => released++);
            tool.Cancelled = _ => { h.Coordinator.ActivateMeasurementTool(next.Id); if (throws) throw new InvalidOperationException(); };
            if (throws) Assert.Throws<InvalidOperationException>(h.Coordinator.Dispose); else h.Coordinator.Dispose();
            h.Coordinator.Dispose();
            Assert.Null(h.Coordinator.ActiveId); Assert.Equal(InteractionMode.Idle, h.Coordinator.Mode);
            Assert.False(h.Layers.Collection.InputSuppressed); Assert.Equal(1, released);
        });
    }

    [Fact]
    public async Task ClosingViewerRejectsPublicStartAndReleasesAllScopes()
    {
        var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        var rejected = false; var released = 0;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var tool = new Tool("closing");
            tool.Cancelled = _ => { Assert.Throws<ObjectDisposedException>(() => viewer.ActivateMeasurementTool(tool.Id)); rejected = true; throw new InvalidOperationException(); };
            viewer.RegisterMeasurementTool(tool); viewer.ActivateMeasurementTool(tool.Id);
            var completed = tool.Context.CreateMeasurement(MeasurementGeometry.Point(new())); completed.OnDispose(() => released++); completed.Complete();
            tool.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => released++);
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
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var h = new Harness();
            var old = new Tool("same");
            var replacement = new Tool("same");
            h.Tools.RegisterTool(old);
            h.Coordinator.ActivateMeasurementTool(old.Id);
            var oldReleased = 0; var replacementReleased = 0;
            old.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => oldReleased++);
            var failure = new InvalidOperationException("outgoing cancellation failed");
            old.Cancelled = _ =>
            {
                h.Tools.RegisterTool(replacement);
                h.Coordinator.ActivateMeasurementTool(replacement.Id);
                replacement.Context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => replacementReleased++);
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
