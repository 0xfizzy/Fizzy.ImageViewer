using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementToolProtocolTests
{
    private sealed class PublicContext : IMeasurementToolContext
    {
        public MeasurementStyle Style { get; } = new();
        public FrameLease? AcquireCurrentFrame() => null;
        public IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options = null) => throw new NotSupportedException();
    }

    private sealed class Probe : IMeasurementTool
    {
        public string Id { get; set; } = "custom";
        public string DisplayName { get; set; } = "Custom";
        public IMeasurementToolContext? Context;
        public Point Point;
        public bool Finish;
        public Exception? Failure;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
            => new TestMeasurementSession(point => OnClick(point, context),
                point => OnMouseMove(point, context), () => Cancel(context));
        public bool OnClick(Point point, IMeasurementToolContext context)
        { Context = context; Point = point; if (Failure != null) throw Failure; return Finish; }
        public void OnMouseMove(Point point, IMeasurementToolContext context)
        { Context = context; Point = point; if (Failure != null) throw Failure; }
        public void Cancel(IMeasurementToolContext context)
        { Context = context; if (Failure != null) throw Failure; }
    }

    [Fact]
    public void ToolRequiresOnlyPublicContextAndPreservesResultsAndExceptions()
    {
        var context = new PublicContext(); var method = new Probe();
        var tool = method.CreateSession(context);
        Assert.NotSame(tool, method.CreateSession(context));
        Assert.False(tool.OnClick(new(1, 2)));
        Assert.Same(context, method.Context); Assert.Equal(new Point(1, 2), method.Point);
        method.Finish = true; Assert.True(tool.OnClick(new(3, 4)));
        method.Context = null; tool.OnMouseMove(new(5, 6));
        Assert.Same(context, method.Context); Assert.Equal(new Point(5, 6), method.Point);
        method.Context = null; tool.Cancel(); Assert.Same(context, method.Context);
        method.Failure = new InvalidOperationException("plugin failure");
        Assert.Same(method.Failure, Assert.Throws<InvalidOperationException>(() => tool.OnClick(new())));
        Assert.Same(method.Failure, Assert.Throws<InvalidOperationException>(() => tool.OnMouseMove(new())));
        Assert.Same(method.Failure, Assert.Throws<InvalidOperationException>(() => tool.Cancel()));
    }

    [Fact]
    public async Task RegistryValidatesBothKindsOfToolsAndSnapshotsMetadata()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.Throws<ArgumentNullException>(() => viewer.RegisterMeasurementTool(null!));
            Assert.Throws<ArgumentException>(() => viewer.RegisterMeasurementTool(new Probe { Id = " " }));
            Assert.Throws<ArgumentException>(() => viewer.RegisterMeasurementTool(new Probe { DisplayName = " " }));
            Assert.Throws<ArgumentException>(() => viewer.RegisterMeasurementTool(new Probe { Id = MeasurementToolIds.Point }));
            var method = new Probe { Id = "point" };
            viewer.RegisterMeasurementTool(method);
            Assert.Throws<ArgumentException>(() => viewer.RegisterMeasurementTool(new Probe { Id = "point" }));
            method.Id = "changed"; method.DisplayName = "Changed";
            viewer.StartMeasurement("point"); viewer.Host.Interaction.ImageDown(1, 2);
            Assert.NotNull(method.Context);
            Assert.Throws<KeyNotFoundException>(() => viewer.StartMeasurement("changed"));
            Assert.True(viewer.UnregisterMeasurementTool("point"));
            Assert.False(viewer.UnregisterMeasurementTool("point"));
            var manager = new MeasurementToolRegistry();
            var original = new Probe(); manager.RegisterTool(original);
            original.Id = "changed"; original.DisplayName = "Changed";
            var entry = Assert.Single(manager.RegisteredTools);
            Assert.Equal("custom", entry.Id); Assert.Equal("Custom", entry.DisplayName);
        });
    }

    [Theory]
    [InlineData(MeasurementToolIds.Point)]
    [InlineData(MeasurementToolIds.Length)]
    [InlineData(MeasurementToolIds.ROI)]
    [InlineData(MeasurementToolIds.LineProfile)]
    public async Task BuiltInsCancelPreviewsAndCompleteThroughUnifiedExecution(string id)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            int completed = 0, removed = 0;
            viewer.MeasurementCompleted += (_, _) => completed++;
            viewer.MeasurementRemoved += (_, _) => removed++;
            viewer.StartMeasurement(id);
            viewer.CancelMeasurement();
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
            viewer.StartMeasurement(id); viewer.Host.Interaction.ImageDown(1, 1);
            if (id != MeasurementToolIds.Point)
            {
                viewer.Host.Interaction.ImageMove(4, 4); viewer.CancelMeasurement();
                Assert.Equal(0, completed); Assert.Equal(0, removed);
                Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
                viewer.StartMeasurement(id); viewer.Host.Interaction.ImageDown(1, 1); viewer.Host.Interaction.ImageDown(4, 4);
            }
            Assert.Equal(1, completed);
            viewer.Layers.Clear();
            Assert.Equal(1, removed);
            Assert.Empty(viewer.Host.Window.MeasurementOverlay.Canvas.Children.Cast<UIElement>());
        });
    }

    private sealed class FrameTool : IMeasurementTool
    {
        public string Id => "frames";
        public string DisplayName => "Frames";
        public int Released;
        public byte Pixel;
        public bool Finish;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
            => new TestMeasurementSession(point => OnClick(point, context),
                point => OnMouseMove(point, context), () => Cancel(context));
        public bool OnClick(Point point, IMeasurementToolContext context)
        {
            using var frame = context.AcquireCurrentFrame();
            Pixel = frame!.CpuPixels.Span[0];
            var scope = context.CreateMeasurement(MeasurementGeometry.Point(new()));

            scope.OnDispose(() => Released++);
            if (Finish) scope.Complete();
            return Finish;
        }
        public void OnMouseMove(Point point, IMeasurementToolContext context) { }
        public void Cancel(IMeasurementToolContext context) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicToolReadsFrameAndMeasurementOwnsCleanup(bool finish)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        var tool = new FrameTool { Finish = finish };
        ImageFrame Frame() => ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 });
        await viewer.SubmitFrameAsync(Frame());
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.RegisterMeasurementTool(tool); viewer.StartMeasurement(tool.Id); viewer.Host.Interaction.ImageDown(0, 0);
            Assert.Equal(42, tool.Pixel);
        });
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.UnregisterMeasurementTool(tool.Id);
            Assert.Equal(finish ? 0 : 1, tool.Released);
        });
        await viewer.DisposeAsync();
        Assert.Equal(1, tool.Released);
    }
}
