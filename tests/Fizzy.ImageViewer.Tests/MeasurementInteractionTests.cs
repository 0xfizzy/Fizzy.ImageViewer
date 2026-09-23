using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements.Methods;
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
    private static OverlayLayer Overlay(Viewer viewer) => viewer.Layers.Measurements.Root.Children.OfType<OverlayLayer>().Single();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionSubscriberCanStartNextMeasurement(bool startRectangle)
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            int completed = 0;
            viewer.MeasurementCompleted += (_, _) =>
            {
                completed++;
                if (completed != 1) return;
                viewer.StartMeasure(startRectangle ? MeasureToolIds.ROI : MeasureToolIds.Point);
                if (startRectangle) viewer.Interaction.ImageDown(2, 2);
            };
            viewer.StartMeasure(MeasureToolIds.Point);
            viewer.Interaction.ImageDown(1, 1);
            Assert.Equal(InteractionMode.Measuring, viewer.Interaction.Mode);
            Assert.True(viewer.Layers.InputSuppressed);
            viewer.Interaction.ImageDown(5, 5);
            Assert.Equal(2, completed);
            Assert.Equal(InteractionMode.Idle, viewer.Interaction.Mode);
            Assert.False(viewer.Layers.InputSuppressed);
        });
    }

    private static MeasurementItem DrawRoi(Viewer viewer)
    {
        var method = new RectTool(viewer.MeasurementContext);
        method.OnClick(new(2, 2));
        method.OnClick(new(6, 6));
        var shape = Overlay(viewer).Canvas.Children.OfType<Rectangle>().Last();
        return viewer.MeasurementContext.Find(shape)!;
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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var item = DrawRoi(viewer);
            var overlay = Overlay(viewer);
            viewer.Interaction.StartEditing(item.PrimaryVisual);
            var editor = viewer.Interaction.Editor;
            var initial = item.Geometry;
            Assert.True(editor.BeginDrag(initial.ControlPoints[index], 1));
            editor.UpdateDrag(new(x, y));
            // Continuing beyond the crossing must keep the ORIGINAL opposite corner fixed.
            editor.UpdateDrag(new(x + .25, y + .25));
            var expected = MeasurementGeometry.Rectangle(initial.ControlPoints[(index + 2) % 4], new(x + .25, y + .25));
            Assert.Equal(expected.Start, item.Geometry.Start); Assert.Equal(expected.End, item.Geometry.End);
            Assert.Equal(2, item.Geometry.Version - initial.Version);
            var rectangle = (Rectangle)item.PrimaryVisual;
            Assert.Equal(expected.Width, rectangle.Width); Assert.Equal(expected.Height, rectangle.Height);
            Assert.Equal(expected.X, Canvas.GetLeft(rectangle)); Assert.Equal(expected.Y, Canvas.GetTop(rectangle));
            Assert.Equal(expected.Start, ((OverlayShapeData)item.Label.Tag).AnchorPoint);
            editor.EndDrag();
            using var frame = viewer.AcquireCurrentFrame();
            var request = Assert.IsType<RegionStatisticsQueryRequest>(item.Capture(frame!.Descriptor));
            Assert.Equal(item.Geometry.ToRegion(frame.Descriptor), request.Region);
            Assert.Equal(item.Geometry.Version, request.Identity.GeometryVersion);
            viewer.FreezeMenuRegion(); frozen = viewer.AcquireMenuRegionSnapshot();
            Assert.NotNull(frozen); Assert.Equal(request.Region, frozen!.Region);
            // The menu captures an immutable region, even if geometry changes afterwards.
            item.UpdateGeometry(MeasurementGeometry.Rectangle(new(0, 0), new(1, 1)));
            Assert.Equal(request.Region, frozen.Region);
            viewer.Unfreeze();
        });
        using var snapshot = await viewer.CaptureSnapshotAsync(frozen!, SnapshotKind.Raw, default);
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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var rectangle = Shapes.CreateRectangle(); rectangle.Width = 4; rectangle.Height = 4;
            Canvas.SetLeft(rectangle, 2); Canvas.SetTop(rectangle, 2);
            var editor = new RectangleEditor();
            var opposite = editor.GetControlPoints(rectangle)[(index + 2) % 4];
            editor.UpdateControlPoint(rectangle, index, new(x, y));
            Assert.Equal(Math.Abs(x - opposite.X), rectangle.Width);
            Assert.Equal(Math.Abs(y - opposite.Y), rectangle.Height);
        });
    }

    [Fact]
    public async Task FailedEditDoesNotChangeValidSessionAndPointLabelsFollowEdits()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = Overlay(viewer); var invalid = new TextBlock();
            viewer.MeasurementContext.AttachVisualInternal(invalid);
            viewer.Interaction.StartEditing(invalid);
            Assert.Equal(InteractionMode.Idle, viewer.Interaction.Mode);
            Assert.Empty(viewer.Interaction.Editor.Handles);
            var tool = new PointTool(viewer.MeasurementContext); tool.OnClick(new(3, 4));
            var point = overlay.Canvas.Children.OfType<System.Windows.Shapes.Path>().Single();
            viewer.Interaction.StartEditing(point);
            Assert.Equal(InteractionMode.Editing, viewer.Interaction.Mode);
            viewer.Interaction.StartEditing(invalid);
            Assert.Same(point, viewer.Interaction.Editor.EditingShape);
            var editor = viewer.Interaction.Editor;
            Assert.True(editor.BeginDrag(new(3, 4), 1)); editor.UpdateDrag(new(7, 8)); editor.EndDrag();
            var item = viewer.MeasurementContext.Find(point)!;
            Assert.Equal(new Point(7, 8), item.Geometry.Start);
            Assert.Equal($"X:{7:F2}\nY:{8:F2}", item.Label.Text);
            viewer.Interaction.DeleteSelected(); Assert.True(item.IsDisposed);
            Assert.Empty(editor.Handles); Assert.Equal(InteractionMode.Idle, viewer.Interaction.Mode);
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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var item = DrawRoi(viewer); var overlay = Overlay(viewer);
            viewer.Layers.Markers.IsHitTestVisible = false;
            viewer.Interaction.StartEditing(item.PrimaryVisual);
            Assert.True(viewer.Interaction.Editor.BeginDrag(item.Geometry.Start, 1));
            viewer.Interaction.Editor.UpdateDrag(new(1, 1));
            switch (action)
            {
                case "measure": viewer.StartMeasure("Length"); Assert.Equal(InteractionMode.Measuring, viewer.Interaction.Mode); break;
                case "delete": viewer.Interaction.DeleteSelected(); break;
                case "clear": viewer.ClearShapes(); break;
                case "hide": viewer.Layers.Measurements.IsVisible = false; break;
                case "disable": viewer.Layers.Measurements.IsHitTestVisible = false; break;
                default: viewer.CancelMeasure(); break;
            }
            Assert.False(viewer.Interaction.Editor.IsEditing);
            Assert.False(viewer.Interaction.Editor.IsDragging);
            Assert.Empty(viewer.Interaction.Editor.Handles);
            Assert.False(overlay.Canvas.IsMouseCaptured);
            viewer.CancelMeasure();
            Assert.Equal(InteractionMode.Idle, viewer.Interaction.Mode);
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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.StartMeasure("ROI");
            viewer.Interaction.ImageDown(1, 1); viewer.Interaction.ImageMove(4, 4);
            var overlay = Overlay(viewer);
            Assert.Equal(2, overlay.Canvas.Children.Count);
            var item = viewer.MeasurementContext.Find(overlay.Canvas.Children.OfType<Rectangle>().Single())!;
            Assert.False(item.IsComplete);
            if (hide) viewer.Layers.Measurements.IsVisible = false;
            else viewer.ClearShapes();
            Assert.True(item.IsDisposed); Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
            Assert.Equal(InteractionMode.Idle, viewer.Interaction.Mode);
            Assert.False(viewer.Layers.InputSuppressed);
        });
    }

    [Fact]
    public async Task ReentrantRemovalAndRepeatedDisposalRaiseEachEventOnce()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var item = DrawRoi(viewer); var overlay = Overlay(viewer);
            int removed = 0;
            overlay.ShapeRemoved += visual => { removed++; viewer.MeasurementContext.RemoveShape(visual); item.Dispose(); };
            item.Dispose(); item.Dispose(); viewer.ClearShapes();
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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            // No physical mouse/foreground-window dependency in the state-machine test.
            var image = new ImageLayer();
            var layers = new Drawing.ViewerLayers(image.TransformGroup);
            var overlay = new OverlayLayer();
            layers.Measurements.Root.Children.Add(overlay);
            using var queries = new PixelQueryScheduler(() => null, NullLogger.Instance, new DispatcherQueryRuntime(overlay.Dispatcher));
            using var measure = new MeasureManager(overlay, () => null, queries, NullLogger.Instance);
            var editor = new EditManager(overlay, measure.Context);
            var capture = new FakeCapture { Succeeds = action != "failed" };
            using var coordinator = new InteractionCoordinator(image, overlay, editor, measure, layers, capture);
            var tool = new RectTool(measure.Context); tool.OnClick(new(2, 2)); tool.OnClick(new(6, 6));
            var shape = overlay.Canvas.Children.OfType<Rectangle>().Single();
            coordinator.StartEditing(shape);
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
        });
    }
}

