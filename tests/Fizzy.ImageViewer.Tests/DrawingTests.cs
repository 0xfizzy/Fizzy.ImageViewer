using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class DrawingTests
{
    private static Viewer Create() => new(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
    private static CircleElement Circle(double x = 20, double y = 20) => new(new(x, y), 5, Brushes.Red, 2, Brushes.Red);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearUsesInitialLayersWhenRemovalSubscriberChangesCollection(bool removeLayer)
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var original = viewer.Layers.CreateLayer("original");
            var originalBatch = original.AddBatch([Circle()]);
            DrawingLayer? replacement = null;
            DrawingBatchHandle? replacementBatch = null;
            int removed = 0;
            viewer.MeasurementRemoved += (_, _) =>
            {
                removed++;
                if (removeLayer) viewer.Layers.RemoveLayer(original);
                replacement = viewer.Layers.CreateLayer("replacement");
                replacementBatch = replacement.AddBatch([Circle()]);
            };
            viewer.StartMeasure(MeasureToolIds.Point);
            viewer.Interaction.ImageDown(1, 1);
            viewer.ClearShapes();
            Assert.Equal(1, removed);
            Assert.NotNull(replacement);
            Assert.Contains(replacement, viewer.Layers.Items);
            Assert.Equal(1, replacement.Host.Count);
            replacementBatch!.Replace([Circle(30, 30)]);
            Assert.Throws<ObjectDisposedException>(() => originalBatch.Replace([Circle()]));
            Assert.Equal(!removeLayer, viewer.Layers.Items.Contains(original));
            viewer.ClearShapes();
            Assert.Equal(0, replacement.Host.Count);
            Assert.Throws<ObjectDisposedException>(() => replacementBatch.Replace([Circle()]));
        });
    }

    private static void Arrange(ViewerLayers layers)
    { layers.Root.Measure(new(800, 600)); layers.Root.Arrange(new(0, 0, 800, 600)); layers.Root.UpdateLayout(); }

    [Theory]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task CollectionUsesOneVisualAndReplacementKeepsIdentity(int count)
    {
        await using var viewer = Create();
        var elements = Enumerable.Range(0, count).Select(i => Circle(i % 100 * 10, i / 100 * 10)).ToArray();
        var batch = viewer.Layers.Markers.AddBatch(elements);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            Arrange(viewer.Layers);
            Assert.Equal(1, VisualTreeHelper.GetChildrenCount(viewer.Layers.Markers.Host));
            Assert.IsType<DrawingVisual>(VisualTreeHelper.GetChild(viewer.Layers.Markers.Host, 0));
            Assert.Equal(count, batch.Elements.Length);
            var visual = VisualTreeHelper.GetChild(viewer.Layers.Markers.Host, 0);
            batch.Replace([Circle(200, 200)]);
            Assert.Same(visual, VisualTreeHelper.GetChild(viewer.Layers.Markers.Host, 0));
            Assert.Single(batch.Elements);
        });
        batch.Dispose(); batch.Dispose();
        await viewer.UiDispatcher.InvokeAsync(() => Assert.Equal(0, viewer.Layers.Markers.Host.Count));
        Assert.Throws<ObjectDisposedException>(() => batch.Replace(elements));
    }

    [Fact]
    public async Task InputsAreSnapshottedAndInvalidReplacementPreservesContent()
    {
        await using var viewer = Create();
        var brush = new SolidColorBrush(Colors.Blue);
        var elements = new List<DrawingElement> { Circle() with { Stroke = brush, Fill = brush } };
        using var batch = viewer.Layers.Markers.AddBatch(elements);
        brush.Color = Colors.Green; elements.Clear();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var element = Assert.IsType<CircleElement>(Assert.Single(batch.Elements));
            Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(element.Fill).Color);
            Assert.True(element.Fill!.IsFrozen);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.Replace([Circle() with { Radius = -1 }]));
        Assert.Throws<ArgumentException>(() => batch.Replace([new LineElement(new(double.NaN, 1), new(), Brushes.Red)]));
        Assert.Throws<ArgumentException>(() => batch.Replace([new TextElement(new(), "x", Brushes.Red) { ScaleMode = OverlayScaleMode.FixedSize }]));
        await viewer.UiDispatcher.InvokeAsync(() => Assert.Single(batch.Elements));
        batch.Replace([]);
        await viewer.UiDispatcher.InvokeAsync(() => { Assert.Empty(batch.Elements); Assert.True(batch.Visual.ContentBounds.IsEmpty); Assert.Equal(1, viewer.Layers.Markers.Host.Count); });
    }

    [Fact]
    public async Task ReplacementKeepsArrayAndMutableBrushSnapshotsIsolated()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var brush = new SolidColorBrush(Colors.Blue);
            DrawingElement[] input = [Circle() with { Stroke = brush, Fill = brush }, Circle(60, 60)];
            using var batch = viewer.Layers.Markers.AddBatch([]);
            batch.Replace(input);
            brush.Color = Colors.Green;
            input[1] = Circle(200, 200);
            {
                var first = Assert.IsType<CircleElement>(batch.Elements[0]);
                Assert.Same(first.Stroke, first.Fill);
                Assert.True(first.Stroke.IsFrozen);
                Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(first.Stroke).Color);
                Assert.Equal(new Point(60, 60), Assert.IsType<CircleElement>(batch.Elements[1]).Center);
                var before = batch.Visual.ContentBounds;
                Assert.Throws<ArgumentException>(() => batch.Replace([Circle(), null!]));
                Assert.Equal(before, batch.Visual.ContentBounds);
            }
            batch.Replace(input);
            Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(((CircleElement)batch.Elements[0]).Stroke).Color);
        });
    }

    [Fact]
    public async Task SharedPensPreserveDifferentWidthsAndScaleModes()
    {
        await using var viewer = Create();
        using var batch = viewer.Layers.Markers.AddBatch([
            Circle(20, 20) with { Thickness = 2 },
            Circle(60, 60) with { Thickness = 8 },
            Circle(100, 100) with { Thickness = 8, ScaleMode = OverlayScaleMode.None }]);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.Layers.UpdateScale(2); viewer.Layers.FlushScale();
            var group = batch.Visual.Drawing;
            var drawings = group.Children.Cast<GeometryDrawing>().ToArray();
            Assert.Equal(new double[] { 1, 4, 8 }, drawings.Select(d => d.Pen.Thickness));
            Assert.All(drawings, d => Assert.True(d.Pen.IsFrozen));
        });
    }

    [Fact]
    public async Task DrawingFailureDoesNotPublishPartialContentAndNextUpdateSucceeds()
    {
        await using var viewer = Create();
        using var batch = viewer.Layers.Markers.AddBatch([Circle()]);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var originalElements = batch.Elements;
            var originalBounds = batch.Visual.ContentBounds;
            // This passes element validation, then fails inside WPF text formatting
            // AFTER the first circle's drawing command has already been recorded.
            DrawingElement[] invalid = [Circle(200, 200), new TextElement(new(), "x", Brushes.White, double.MaxValue)];
            Assert.Throws<ArgumentOutOfRangeException>(() => batch.Replace(invalid));
            Assert.Same(originalElements, batch.Elements);
            Assert.Equal(originalBounds, batch.Visual.ContentBounds);
            Assert.NotNull(VisualTreeHelper.HitTest(batch.Visual, new Point(20, 20)));
            Assert.Null(VisualTreeHelper.HitTest(batch.Visual, new Point(200, 200)));
            Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Layers.Markers.AddBatch(invalid));
            Assert.Equal(1, viewer.Layers.Markers.Host.Count);
            batch.Replace([Circle(200, 200)]);
            Assert.Null(VisualTreeHelper.HitTest(batch.Visual, new Point(20, 20)));
            Assert.NotNull(VisualTreeHelper.HitTest(batch.Visual, new Point(200, 200)));
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DirectCommandsMatchDrawingGroupPixels(double scale)
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var translucent = new SolidColorBrush(Color.FromArgb(110, 30, 200, 80));
            translucent.Freeze();
            DrawingElement[] elements = [
                new CircleElement(new(40, 40), 20, Brushes.Red, 3, translucent),
                new CircleElement(new(55, 40), 20, Brushes.Red, 3, translucent),
                new RectangleElement(new(20, 55, 70, 30), Brushes.Yellow, 4, translucent),
                new LineElement(new(5, 5), new(120, 95), Brushes.Blue, 2),
                new CrosshairElement(new(100, 30), Brushes.White, 12, 2),
                new TextElement(new(10, 110), "Camera 123", Brushes.White, 16, new(2, -3))];
            var layers = new ViewerLayers(Transform.Identity);
            layers.UpdateScale(scale);
            layers.FlushScale();
            using var batch = layers.Markers.AddBatch(elements);
            // Reference path used before direct command recording.
            var group = new DrawingGroup();
            var resources = new DrawingResources();
            using (var context = group.Open())
                foreach (var element in elements) element.Draw(context, scale, layers.Markers.PixelsPerDip, resources);
            group.Freeze();
            var reference = new DrawingVisual();
            using (var context = reference.RenderOpen()) context.DrawDrawing(group);
            byte[] Pixels(Visual visual)
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(160, 140, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var pixels = new byte[160 * 140 * 4];
                bitmap.CopyPixels(pixels, 160 * 4, 0);
                return pixels;
            }
            Assert.Equal(reference.ContentBounds, batch.Visual.ContentBounds);
            Assert.Equal(Pixels(reference), Pixels(batch.Visual));
            layers.Close();
        });
    }

    [Fact]
    public async Task HitTestingUsesDrawingContentAndLayerState()
    {
        await using var viewer = Create();
        var layers = await viewer.UiDispatcher.InvokeAsync(() => new ViewerLayers(Transform.Identity));
        var markers = layers.Markers;
        using var a = markers.AddBatch([Circle()]);
        using var b = markers.AddBatch([Circle()]);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Drawing tests") { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = layers.Root;
            Arrange(layers);
            Assert.Null(markers.HitBatch(new(20, 20)));
            Assert.NotSame(markers.Host, layers.Root.InputHitTest(new(20, 20)));
            markers.IsHitTestVisible = true;
            Assert.Same(b, markers.HitBatch(new(20, 20)));
            BatchClickedEventArgs? clicked = null;
            markers.BatchClicked += (_, e) => { Assert.True(viewer.UiDispatcher.CheckAccess()); clicked = e; };
            Assert.True(markers.DispatchClick(new(20, 20), System.Windows.Input.MouseButton.Left));
            Assert.Same(b, clicked!.Batch); Assert.Equal(new Point(20, 20), clicked.ImagePosition);
            Assert.Equal(System.Windows.Input.MouseButton.Left, clicked.Button);
            Assert.False(markers.DispatchClick(new(100, 100), System.Windows.Input.MouseButton.Left));
            Assert.Same(markers.Host, layers.Root.InputHitTest(new(20, 20)));
            Assert.Null(markers.HitBatch(new(100, 100)));
            Assert.NotSame(markers.Host, layers.Root.InputHitTest(new(100, 100)));
            b.Dispose(); Assert.Same(a, markers.HitBatch(new(20, 20)));
            using var outline = markers.AddBatch([Circle(100, 100) with { Fill = null }]);
            Assert.Null(markers.HitBatch(new(100, 100)));
            Assert.Same(outline, markers.HitBatch(new(105, 100)));
            markers.IsVisible = false; Assert.Null(markers.HitBatch(new(20, 20)));
            Assert.NotSame(markers.Host, layers.Root.InputHitTest(new(20, 20)));
            markers.IsVisible = true;
            var top = layers.CreateLayer("top"); top.IsHitTestVisible = true;
            top.AddBatch([Circle()]); Arrange(layers);
            Assert.Same(top.Host, layers.Root.InputHitTest(new(20, 20)));
            markers.ZIndex = top.ZIndex;
            Assert.Same(top.Host, layers.Root.InputHitTest(new(20, 20)));
            markers.ZIndex = top.ZIndex + 1;
            Assert.Same(markers.Host, layers.Root.InputHitTest(new(20, 20)));
        });
    }

    [Fact]
    public async Task MeasurementSuppressionRestoresConfiguredFlagsIncludingNewLayers()
    {
        await using var viewer = Create();
        var enabled = viewer.Layers.CreateLayer("enabled"); enabled.IsHitTestVisible = true;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.StartMeasure("Length");
            Assert.True(viewer.Layers.InputSuppressed);
            Assert.False(enabled.Root.IsHitTestVisible);
            Assert.True(enabled.IsHitTestVisible);
            Assert.False(viewer.Layers.Markers.IsHitTestVisible);
            var during = viewer.Layers.CreateLayer("during"); during.IsHitTestVisible = true;
            Assert.False(during.Root.IsHitTestVisible);
            viewer.Layers.Measurements.IsHitTestVisible = false;
            viewer.CancelMeasure();
            Assert.False(viewer.Layers.InputSuppressed);
            Assert.True(enabled.Root.IsHitTestVisible);
            Assert.True(during.Root.IsHitTestVisible);
            Assert.False(viewer.Layers.Markers.Root.IsHitTestVisible);
            Assert.False(viewer.Layers.Measurements.Root.IsHitTestVisible);
            var parent = (Grid)VisualTreeHelper.GetParent(viewer.Layers.Root);
            var image = parent.Children.OfType<ImageLayer>().Single();
            viewer.StartMeasure("Point");
            image.Container.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert.False(viewer.Layers.InputSuppressed);
            Assert.True(enabled.Root.IsHitTestVisible);
            Assert.False(viewer.Layers.Measurements.Root.IsHitTestVisible);
            viewer.StartMeasure("Length");
            viewer.Layers.Measurements.Clear();
            Assert.False(viewer.Layers.InputSuppressed);
        });
    }

    [Fact]
    public async Task ScaleModesPreserveCustomSizesAndPositions()
    {
        await using var viewer = Create();
        using var fixedStroke = viewer.Layers.Markers.AddBatch([Circle(100, 100) with { Radius = 10, Thickness = 6 }]);
        using var fixedSize = viewer.Layers.Markers.AddBatch([Circle(100, 100) with { Radius = 10, Thickness = 6, ScaleMode = OverlayScaleMode.FixedSize }]);
        using var scaled = viewer.Layers.Markers.AddBatch([Circle(100, 100) with { Radius = 10, Thickness = 6, ScaleMode = OverlayScaleMode.None }]);
        using var mixed = viewer.Layers.Markers.AddBatch([
            new LineElement(new(1, 1), new(20, 1), Brushes.Green, 5),
            new RectangleElement(new(50, 50, 30, 30), Brushes.Blue, 4),
            new CrosshairElement(new(200, 200), Brushes.Yellow, 7, 3),
            new TextElement(new(30, 30), "Hello", Brushes.White, 24, new(5, 6))]);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var before = scaled.Visual.ContentBounds;
            viewer.Layers.UpdateScale(2); viewer.Layers.UpdateScale(4); viewer.Layers.FlushScale();
            Assert.Equal(21.5, fixedStroke.Visual.ContentBounds.Width, 5);
            Assert.Equal(6.5, fixedSize.Visual.ContentBounds.Width, 5);
            Assert.Equal(100, fixedSize.Visual.ContentBounds.X + fixedSize.Visual.ContentBounds.Width / 2, 5);
            Assert.Equal(26, scaled.Visual.ContentBounds.Width, 5);
            Assert.Equal(before, scaled.Visual.ContentBounds);
            Assert.False(mixed.Visual.ContentBounds.IsEmpty);
        });
    }

    [Fact]
    public async Task ClearRemoveAndCloseInvalidateHandlesAndKeepHud()
    {
        var viewer = Create();
        await using var other = Create();
        try
        {
            var custom = viewer.Layers.CreateLayer("custom");
            Assert.Throws<ArgumentException>(() => viewer.Layers.CreateLayer("custom"));
            Assert.Throws<ArgumentException>(() => other.Layers.RemoveLayer(custom));
            Assert.Throws<InvalidOperationException>(() => viewer.Layers.RemoveLayer(viewer.Layers.Markers));
            var batch = custom.AddBatch([Circle()]);
            viewer.Layers.RemoveLayer(custom);
            Assert.Throws<ObjectDisposedException>(() => batch.Replace([Circle()]));
            Assert.Throws<ObjectDisposedException>(() => custom.Clear()); batch.Dispose();
            var marker = viewer.Layers.Markers.AddBatch([Circle()]);
            var measure = viewer.Layers.Measurements.AddBatch([Circle()]);
            using var hud = viewer.DrawHudText("HUD", Brushes.White);
            viewer.ClearShapes();
            hud.Update("HUD 2", Brushes.White);
            Assert.Throws<ObjectDisposedException>(() => marker.Replace([Circle()]));
            Assert.Throws<ObjectDisposedException>(() => measure.Replace([Circle()]));
            var last = viewer.Layers.Markers.AddBatch([Circle()]);
            await viewer.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => last.Replace([Circle()]));
            last.Dispose();
            Assert.Throws<ObjectDisposedException>(() => viewer.Layers.CreateLayer("closed"));
        }
        finally { await viewer.DisposeAsync(); }
    }

    [Fact]
    public async Task MeasurementSelectionEditingAndLinkedRemovalStillWork()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Layers.Measurements.Root.Children.OfType<OverlayLayer>().Single();
            foreach (var shape in new UIElement[] { Shapes.CreatePoint(new(30, 30)), Shapes.CreateLine(), Shapes.CreateRectangle() })
            {
                viewer.MeasurementContext.AttachVisualInternal(shape);
                viewer.Interaction.Select(shape); viewer.Interaction.StartEditing(viewer.Interaction.SelectedShape!);
                var data = OverlayShapeData.Get(shape)!;
                Assert.NotEmpty(viewer.Interaction.Editor.Handles);
                var editor = viewer.Interaction.Editor;
                Assert.True(editor.BeginDrag(data.AnchorPoint, 1));
                editor.UpdateDrag(new(40, 40)); editor.EndDrag();

                viewer.Interaction.DeleteSelected();
                Assert.Null(viewer.Interaction.SelectedShape);
                Assert.Empty(viewer.Interaction.Editor.Handles);
                Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
            }
            var line = Shapes.CreateLine(); var label = Shapes.CreateLabel(new(1, 1), "length");
            new MeasurementItem(viewer.MeasurementContext, MeasurementGeometry.Line(new(0,0), new(1,1)), line, label).Complete();
            viewer.MeasurementContext.AttachVisualInternal(line); viewer.MeasurementContext.AttachVisualInternal(label);
            viewer.Interaction.Select(line); viewer.Interaction.DeleteSelected();
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
        });
    }
    [Fact]
    public async Task MeasurementPreviewCompletionAndClearReleaseLinkedResults()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var overlay = viewer.Layers.Measurements.Root.Children.OfType<OverlayLayer>().Single();
            foreach (Measurements.IMeasureTool method in new Measurements.IMeasureTool[] {
                new Measurements.Methods.PointTool(viewer.MeasurementContext), new Measurements.Methods.LineTool(viewer.MeasurementContext), new Measurements.Methods.RectTool(viewer.MeasurementContext) })
            {
                bool done = method.OnClick(new(10, 10));
                if (!done)
                {
                    method.OnMouseMove(new(50, 50));
                    Assert.NotEmpty(overlay.Canvas.Children.Cast<UIElement>());
                    method.Cancel();
                    Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
                    Assert.False(method.OnClick(new(10, 10)));
                    Assert.True(method.OnClick(new(50, 50)));
                }
                Assert.NotEmpty(overlay.Canvas.Children.Cast<UIElement>());
                viewer.Layers.Measurements.Clear();
                Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
            }
        });
    }
    [Fact]
    public async Task DisabledBatchPassesInputToImageAndTracksZoomAndPan()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var image = new ImageLayer();
            var layers = new ViewerLayers(image.TransformGroup);
            image.ScaleChanged += layers.UpdateScale;
            var root = new Grid(); root.Children.Add(image); root.Children.Add(layers.Root);
            using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Input tests")
                { Width = 800, Height = 600, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = root;
            root.Measure(new(800, 600)); root.Arrange(new(0, 0, 800, 600)); root.UpdateLayout();
            using var batch = layers.Markers.AddBatch([Circle()]);
            Assert.Same(image.Container, root.InputHitTest(new(20, 20)));
            image.Container.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, 120)
                { RoutedEvent = UIElement.MouseWheelEvent });
            Assert.True(image.Scaler.ScaleX > 1);
            image.Panner.X += 30; image.Panner.Y += 50;
            layers.FlushScale();
            var expected = image.ImageToContainer(new(20, 20));
            var actual = layers.Markers.Host.TranslatePoint(new(20, 20), image.Container);
            Assert.Equal(expected.X, actual.X, 6); Assert.Equal(expected.Y, actual.Y, 6);
            Assert.Same(image.Container, root.InputHitTest(expected));
            layers.Close();
        });
    }
}

