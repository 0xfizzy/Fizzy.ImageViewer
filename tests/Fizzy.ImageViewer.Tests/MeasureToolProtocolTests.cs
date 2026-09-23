using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasureToolProtocolTests
{
    private sealed class PublicContext : IMeasureToolContext
    {
        public FrameLease? AcquireCurrentFrame() => null;
        public IMeasurementScope CreateScope() => throw new NotSupportedException();
        public event Action<FrameInfo>? FrameCommitted { add { } remove { } }
    }

    private sealed class Probe : IMeasureMethod
    {
        public string Id { get; set; } = "custom";
        public string DisplayName { get; set; } = "Custom";
        public IMeasureToolContext? Context;
        public Point Point;
        public bool Finish;
        public Exception? Failure;
        public bool OnClick(Point point, IMeasureToolContext context)
        { Context = context; Point = point; if (Failure != null) throw Failure; return Finish; }
        public void OnMouseMove(Point point, IMeasureToolContext context)
        { Context = context; Point = point; if (Failure != null) throw Failure; }
        public void Cancel(IMeasureToolContext context)
        { Context = context; if (Failure != null) throw Failure; }
    }

    [Fact]
    public void AdapterRequiresOnlyPublicContextAndPreservesResultsAndExceptions()
    {
        var context = new PublicContext(); var method = new Probe();
        var tool = new CustomMeasureTool(method, context);
        Assert.Equal(method.Id, tool.Id); Assert.Equal(method.DisplayName, tool.DisplayName);
        Assert.False(tool.OnClick(new(1, 2)));
        Assert.Same(context, method.Context); Assert.Equal(new Point(1, 2), method.Point);
        method.Finish = true; Assert.True(tool.OnClick(new(3, 4)));
        method.Context = null; tool.OnMouseMove(new(5, 6));
        Assert.Same(context, method.Context); Assert.Equal(new Point(5, 6), method.Point);
        method.Context = null; tool.Cancel(); Assert.Same(context, method.Context);
        method.Failure = new InvalidOperationException("plugin failure");
        Assert.Same(method.Failure, Assert.Throws<InvalidOperationException>(() => tool.OnClick(new())));
        Assert.Same(method.Failure, Assert.Throws<InvalidOperationException>(() => tool.OnMouseMove(new())));
        Assert.Same(method.Failure, Assert.Throws<InvalidOperationException>(tool.Cancel));
    }

    [Fact]
    public async Task RegistryValidatesBothKindsOfToolsAndSnapshotsMetadata()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            Assert.Throws<ArgumentNullException>(() => viewer.RegisterMeasureMethod(null!));
            Assert.Throws<ArgumentException>(() => viewer.RegisterMeasureMethod(new Probe { Id = " " }));
            Assert.Throws<ArgumentException>(() => viewer.RegisterMeasureMethod(new Probe { DisplayName = " " }));
            Assert.Throws<ArgumentException>(() => viewer.RegisterMeasureMethod(new Probe { Id = MeasureToolIds.Point }));
            var method = new Probe { Id = "point" };
            viewer.RegisterMeasureMethod(method);
            Assert.Throws<ArgumentException>(() => viewer.RegisterMeasureMethod(new Probe { Id = "point" }));
            method.Id = "changed"; method.DisplayName = "Changed";
            viewer.StartMeasure("point"); viewer.Interaction.ImageDown(1, 2);
            Assert.NotNull(method.Context);
            Assert.Throws<KeyNotFoundException>(() => viewer.StartMeasure("changed"));
            Assert.True(viewer.UnregisterMeasureMethod("point"));
            Assert.False(viewer.UnregisterMeasureMethod("point"));
            using var manager = new MeasureManager(new Controls.OverlayLayer(), () => null, NullLogger.Instance);
            var original = new Probe(); manager.RegisterMethod(original);
            original.Id = "changed"; original.DisplayName = "Changed";
            var entry = Assert.Single(manager.RegisteredMethods);
            Assert.Equal("custom", entry.Id); Assert.Equal("Custom", entry.DisplayName);
        });
    }

    [Theory]
    [InlineData(MeasureToolIds.Point)]
    [InlineData(MeasureToolIds.Length)]
    [InlineData(MeasureToolIds.ROI)]
    [InlineData(MeasureToolIds.LineStrength)]
    public async Task BuiltInsCancelPreviewsAndCompleteThroughUnifiedExecution(string id)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            int completed = 0, removed = 0;
            viewer.MeasurementCompleted += (_, _) => completed++;
            viewer.MeasurementRemoved += (_, _) => removed++;
            viewer.StartMeasure(id);
            viewer.CancelMeasure();
            Assert.Empty(viewer.Layers.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
            viewer.StartMeasure(id); viewer.Interaction.ImageDown(1, 1);
            if (id != MeasureToolIds.Point)
            {
                viewer.Interaction.ImageMove(4, 4); viewer.CancelMeasure();
                Assert.Equal(0, completed); Assert.Equal(0, removed);
                Assert.Empty(viewer.Layers.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
                viewer.StartMeasure(id); viewer.Interaction.ImageDown(1, 1); viewer.Interaction.ImageDown(4, 4);
            }
            Assert.Equal(1, completed);
            viewer.ClearShapes();
            Assert.Equal(1, removed);
            Assert.Empty(viewer.Layers.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
        });
    }

    private sealed class FrameTool : IMeasureMethod
    {
        public string Id => "frames";
        public string DisplayName => "Frames";
        public int Notifications, Released;
        public byte Pixel;
        public bool Finish;
        public bool OnClick(Point point, IMeasureToolContext context)
        {
            using var frame = context.AcquireCurrentFrame();
            Pixel = frame!.CpuPixels.Span[0];
            var scope = context.CreateScope();
            scope.AddShape(Shapes.CreatePoint(point));
            void Changed(FrameInfo _) => Notifications++;
            context.FrameCommitted += Changed;
            scope.OnDispose(() => { context.FrameCommitted -= Changed; Released++; });
            if (Finish) scope.Complete();
            return Finish;
        }
        public void OnMouseMove(Point point, IMeasureToolContext context) { }
        public void Cancel(IMeasureToolContext context) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicToolReadsFrameAndScopeOwnsNotificationLifetime(bool finish)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        var tool = new FrameTool { Finish = finish };
        ImageFrame Frame() => ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 });
        await viewer.SubmitFrameAsync(Frame());
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.RegisterMeasureMethod(tool); viewer.StartMeasure(tool.Id); viewer.Interaction.ImageDown(0, 0);
            Assert.Equal(42, tool.Pixel);
        });
        await viewer.SubmitFrameAsync(Frame());
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            Assert.Equal(1, tool.Notifications);
            viewer.UnregisterMeasureMethod(tool.Id);
            Assert.Equal(finish ? 0 : 1, tool.Released);
        });
        await viewer.SubmitFrameAsync(Frame());
        await viewer.UiDispatcher.InvokeAsync(() => Assert.Equal(finish ? 2 : 1, tool.Notifications));
        await viewer.DisposeAsync();
        Assert.Equal(1, tool.Released);
    }
}
