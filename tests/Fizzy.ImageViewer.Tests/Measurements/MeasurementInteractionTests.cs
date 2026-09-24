using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Menus;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Measurements.Editing;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements.BuiltIn;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementInteractionTests
{
    private static Viewer Create() => new(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
    private static MeasurementOverlay Overlay(Viewer viewer) => viewer.Layers.Measurements.Root.Children.OfType<MeasurementOverlay>().Single();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionSubscriberCanStartNextMeasurement(bool startRectangle)
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            int completed = 0;
            viewer.MeasurementCompleted += (_, _) =>
            {
                completed++;
                if (completed != 1) return;
                viewer.StartMeasurement(startRectangle ? MeasurementToolIds.ROI : MeasurementToolIds.Point);
                if (startRectangle) viewer.Host.Interaction.ImageDown(2, 2);
            };
            viewer.StartMeasurement(MeasurementToolIds.Point);
            viewer.Host.Interaction.ImageDown(1, 1);
            Assert.Equal(InteractionMode.Measuring, viewer.Host.Interaction.Mode);
            Assert.True(viewer.Layers.InputSuppressed);
            viewer.Host.Interaction.ImageDown(5, 5);
            Assert.Equal(2, completed);
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.InputSuppressed);
        });
    }

    private static MeasurementItem DrawRoi(Viewer viewer)
    {
        viewer.StartMeasurement(MeasurementToolIds.ROI);
        viewer.Host.Interaction.ImageDown(2, 2);
        viewer.Host.Interaction.ImageDown(6, 6);
        var shape = Overlay(viewer).Canvas.Children.OfType<Rectangle>().Last();
        return viewer.Host.Measurements.Find(shape)!;
    }

    [Theory]
    [InlineData(0, 8, 9)]
    [InlineData(1, 0, 9)]
    [InlineData(2, 0, 0)]
    [InlineData(3, 8, 0)]
    public async Task CrossingEveryCornerKeepsGeometryQueryAndExportConsistent(int index, double x, double y)
    {
        await using var viewer = Create();
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(10, 10, 10, FramePixelFormat.Gray8), Enumerable.Range(0, 100).Select(i => (byte)i).ToArray()));
        MenuSnapshotSession.Target? frozen = null;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = DrawRoi(viewer);
            var overlay = Overlay(viewer);
            viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(item.Presentation.PrimaryVisual));
            var editor = viewer.Host.Interaction.Editor;
            var initial = item.Geometry; var initialVersion = item.GeometryVersion;
            Assert.True(editor.BeginDrag(initial.ControlPoints[index], 1));
            editor.UpdateDrag(new(x, y));
            // Continuing beyond the crossing must keep the ORIGINAL opposite corner fixed.
            editor.UpdateDrag(new(x + .25, y + .25));
            var expected = MeasurementGeometry.Rectangle(initial.ControlPoints[(index + 2) % 4], new(x + .25, y + .25));
            Assert.Equal(expected.Start, item.Geometry.Start); Assert.Equal(expected.End, item.Geometry.End);
            Assert.Equal(2, item.GeometryVersion - initialVersion);
            var rectangle = (Rectangle)item.Presentation.PrimaryVisual;
            Assert.Equal(expected.Bounds.Width, rectangle.Width); Assert.Equal(expected.Bounds.Height, rectangle.Height);
            Assert.Equal(expected.X, Canvas.GetLeft(rectangle)); Assert.Equal(expected.Y, Canvas.GetTop(rectangle));
            Assert.Equal(expected.Start, OverlayShapeData.Get(item.Presentation.Label)!.AnchorPoint);
            editor.EndDrag();
            using var frame = viewer.AcquireCurrentFrame();
            var request = Assert.IsType<RegionStatisticsQueryRequest>(((IFrameQueryClient)item).Capture(frame!.Descriptor));
            Assert.Equal(item.Geometry.ToRegion(frame.Descriptor), request.Region);
            Assert.Equal(item.GeometryVersion, request.Identity.GeometryVersion);
            viewer.Host.MenuController.CaptureTarget(); frozen = viewer.Host.MenuSession.AcquireTarget(region: true);
            Assert.NotNull(frozen); Assert.Equal(request.Region, frozen!.Region);
            // The menu captures an immutable region, even if geometry changes afterwards.
            item.UpdateGeometry(MeasurementGeometry.Rectangle(new(0, 0), new(1, 1)));
            Assert.Equal(request.Region, frozen.Region);
            viewer.Host.MenuSession.Close();
        });
        using var target = frozen!;
        using var snapshot = await viewer.Host.Snapshots.CaptureAsync(target.View.Acquire(), SnapshotKind.Raw, target.Region);
        using var pixels = snapshot.AcquirePixels();
        Assert.Equal(frozen!.Region!.Value.Width, pixels.Descriptor.Width);
        Assert.Equal(frozen.Region.Value.Height, pixels.Descriptor.Height);
    }

    [Theory]
    [InlineData(0, 8, 9)]
    [InlineData(1, 0, 9)]
    [InlineData(2, 0, 0)]
    [InlineData(3, 8, 0)]
    public async Task StandaloneRectangleEditorNeverAssignsNegativeDimensions(int index, double x, double y)
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationSession(viewer.Host.Measurements).CreateMeasurement(MeasurementGeometry.Rectangle(new(2, 2), new(6, 6)));
            var rectangle = (Rectangle)item.Presentation.PrimaryVisual;
            using var editor = new MeasurementEditSession(item);
            var opposite = editor.Points[(index + 2) % 4];
            editor.BeginDrag(index); editor.Update(new(x, y));
            Assert.Equal(Math.Abs(x - opposite.X), rectangle.Width);
            Assert.Equal(Math.Abs(y - opposite.Y), rectangle.Height);
        });
    }

    [Fact]
    public async Task FailedEditDoesNotChangeValidSessionAndPointLabelsFollowEdits()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var overlay = Overlay(viewer); var invalid = new TextBlock();
            viewer.Host.Window.MeasurementOverlay.AddShape(invalid);
            viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(invalid));
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.Empty(viewer.Host.Interaction.Editor.Handles);
            viewer.StartMeasurement(MeasurementToolIds.Point); viewer.Host.Interaction.ImageDown(3, 4);
            var point = overlay.Canvas.Children.OfType<System.Windows.Shapes.Path>().Single();
            viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(point));
            Assert.Equal(InteractionMode.Editing, viewer.Host.Interaction.Mode);
            viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(invalid));
            Assert.Same(viewer.Host.Measurements.Find(point), viewer.Host.Interaction.Editor.EditingMeasurement);
            var editor = viewer.Host.Interaction.Editor;
            Assert.True(editor.BeginDrag(new(3, 4), 1)); editor.UpdateDrag(new(7, 8)); editor.EndDrag();
            var item = viewer.Host.Measurements.Find(point)!;
            Assert.Equal(new Point(7, 8), item.Geometry.Start);
            Assert.Equal($"X:{7:F2}\nY:{8:F2}", item.Presentation.Label.Text);
            viewer.Host.Interaction.DeleteSelected(); Assert.True(item.IsDisposed);
            Assert.Empty(editor.Handles); Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
        });
    }

    [Theory]
    [InlineData("measure")]
    [InlineData("delete")]
    [InlineData("clear")]
    [InlineData("hide")]
    [InlineData("disable")]
    [InlineData("cancel")]
    public async Task InterruptingDragRestoresInputAndPreservesLayerPreferences(string action)
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = DrawRoi(viewer); var overlay = Overlay(viewer);
            viewer.Layers.Markers.IsHitTestVisible = false;
            viewer.Host.Interaction.StartEditing(viewer.Host.Measurements.Find(item.Presentation.PrimaryVisual));
            Assert.True(viewer.Host.Interaction.Editor.BeginDrag(item.Geometry.Start, 1));
            viewer.Host.Interaction.Editor.UpdateDrag(new(1, 1));
            switch (action)
            {
                case "measure": viewer.StartMeasurement("Length"); Assert.Equal(InteractionMode.Measuring, viewer.Host.Interaction.Mode); break;
                case "delete": viewer.Host.Interaction.DeleteSelected(); break;
                case "clear": viewer.Layers.Clear(); break;
                case "hide": viewer.Layers.Measurements.IsVisible = false; break;
                case "disable": viewer.Layers.Measurements.IsHitTestVisible = false; break;
                default: viewer.CancelMeasurement(); break;
            }
            Assert.False(viewer.Host.Interaction.Editor.IsEditing);
            Assert.False(viewer.Host.Interaction.Editor.IsDragging);
            Assert.Empty(viewer.Host.Interaction.Editor.Handles);
            Assert.False(overlay.Canvas.IsMouseCaptured);
            viewer.CancelMeasurement();
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.InputSuppressed);
            Assert.False(viewer.Layers.Markers.IsHitTestVisible);
            Assert.Equal(action != "disable", viewer.Layers.Measurements.IsHitTestVisible);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MeasurementPreviewIsOwnedAndRemovedOnClearOrHide(bool hide)
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.StartMeasurement("ROI");
            viewer.Host.Interaction.ImageDown(1, 1); viewer.Host.Interaction.ImageMove(4, 4);
            var overlay = Overlay(viewer);
            Assert.Equal(2, overlay.Canvas.Children.Count);
            var item = viewer.Host.Measurements.Find(overlay.Canvas.Children.OfType<Rectangle>().Single())!;
            Assert.False(item.IsComplete);
            if (hide) viewer.Layers.Measurements.IsVisible = false;
            else viewer.Layers.Clear();
            Assert.True(item.IsDisposed); Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.InputSuppressed);
        });
    }

    [Fact]
    public async Task ReentrantRemovalAndRepeatedDisposalRaiseEachEventOnce()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = DrawRoi(viewer); var overlay = Overlay(viewer);
            int removed = 0;
            overlay.ShapeRemoved += visual => { removed++; viewer.Host.Measurements.Find(visual)?.Dispose(); item.Dispose(); };
            item.Dispose(); item.Dispose(); viewer.Layers.Clear();
            Assert.Equal(2, removed); Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
        });
    }

    private sealed class FakeCapture : IMouseCapture
    {
        public bool IsCaptured { get; private set; }
        public bool Succeeds = true;
        public int Releases;
        public bool Capture() => IsCaptured = Succeeds;
        public void Release() { IsCaptured = false; Releases++; }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("lost")]
    [InlineData("failed")]
    [InlineData("delete")]
    [InlineData("close")]
    public async Task CaptureBoundariesRestoreDragState(string action)
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            // No physical mouse/foreground-window dependency in the state-machine test.
            var image = new ImageLayer();
            var layers = new Layers.ViewerLayers(image.TransformGroup);
            var overlay = layers.Measurements.Overlay;
            using var queries = new PixelQueryScheduler(() => null, NullLogger.Instance, new DispatcherQueryRuntime(overlay.Dispatcher));
            var context = new MeasurementStore(layers.Measurements, () => null, queries, NullLogger.Instance);
            var tools = new MeasurementToolRegistry();
            var editor = new EditManager(overlay);
            var capture = new FakeCapture { Succeeds = action != "failed" };
            using var coordinator = new InteractionCoordinator(new ViewerInputBinding(image, overlay, capture), overlay, editor, tools, context, layers);
            var tool = new RectangleRoiTool();
            var creation = new MeasurementCreationSession(context);
            var session = tool.CreateSession(creation);
            session.OnClick(new(2, 2)); session.OnClick(new(6, 6));
            creation.End(); creation.ClearPreviews();
            var shape = overlay.Canvas.Children.OfType<Rectangle>().Single();
            coordinator.StartEditing(context.Find(shape));
            Assert.Equal(action != "failed", coordinator.BeginDrag(new(2, 2)));
            switch (action)
            {
                case "lost":
                    capture.Release();
                    overlay.Canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
                    break;
                case "delete": coordinator.DeleteSelected(); break;
                case "close": coordinator.Dispose(); break;
                case "cancel": coordinator.Cancel(); break;
            }
            Assert.False(capture.IsCaptured); Assert.False(editor.IsDragging);
            Assert.Equal(action == "failed" ? 0 : 1, capture.Releases);
            Assert.Same(Cursors.Cross, image.Container.Cursor);
            Assert.False(layers.InputSuppressed);
            Assert.Equal(action is "lost" or "failed" ? InteractionMode.Editing : InteractionMode.Idle, coordinator.Mode);
            context.Shutdown();
        });
    }
}

