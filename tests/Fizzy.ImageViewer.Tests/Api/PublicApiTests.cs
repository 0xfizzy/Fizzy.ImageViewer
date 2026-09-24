using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class PublicApiTests
{
    private static Viewer Create() => new(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);

    [Fact]
    public async Task InterfaceControlsWindowFromWorkerAndRetainsHiddenContent()
    {
        await using var viewer = Create();
        IViewer api = viewer;
        IViewerWindow window = api;
        IFrameSink sink = api;
        ICommittedFrameSource frames = api;
        Fizzy.ImageViewer.Hud.IViewerHud hud = api;
        Fizzy.ImageViewer.Imaging.IViewerQueries queries = api;
        IViewerDrawing drawing = api;
        Fizzy.ImageViewer.Viewport.IViewerDisplay display = api;
        Fizzy.ImageViewer.Snapshots.ISnapshotSource snapshots = api;
        Fizzy.ImageViewer.Menus.IViewerMenu menus = api;
        IDisposable? registration = null;
        await Task.Run(async () =>
        {
            hud.HudLabelText = "camera";
            Assert.Equal("camera", hud.HudLabelText);
            Assert.True(hud.IsPixelInfoEnabled);
            hud.IsPixelInfoEnabled = false;
            Assert.False(hud.IsPixelInfoEnabled);
            queries.QueryOptions = new();
            Assert.NotNull(queries.QueryOptions);
            _ = queries.QueryMetrics;
            display.DisplayRange = new(0, 255);
            Assert.Equal(display.DisplayRange, viewer.DisplayRange);
            registration = menus.RegisterMenuItem(new Fizzy.ImageViewer.Menus.MenuItem("Inspect", () => { }));
            window.Show();
            Assert.True(window.IsVisible);
            window.Minimize();
            Assert.True(window.IsMinimized);
            Assert.True(window.IsVisible);
            window.Show();
            Assert.False(window.IsMinimized);
            window.Hide();
            Assert.False(window.IsVisible);
            var result = await sink.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }));
            Assert.Equal(FrameSubmitStatus.Committed, result.Status);
            using var line = drawing.DrawLine(new(0, 0), new(1, 1), Brushes.Red);
            window.Show();
            using var lease = frames.AcquireCurrentFrame();
            Assert.Equal(42, lease!.CpuPixels.Span[0]);
            display.FitToViewport();
            using var snapshot = await snapshots.CaptureSnapshotAsync(Fizzy.ImageViewer.Snapshots.SnapshotKind.Raw);
            using var pixels = snapshot.AcquirePixels();
            Assert.Equal(lease.Info.FrameId, snapshot.SourceFrame.FrameId);
            Assert.Equal(42, pixels.CpuPixels.Span[0]);
        });
        await api.DisposeAsync();
        registration!.Dispose();
        registration.Dispose();
        Assert.Throws<ObjectDisposedException>(window.Hide);
        Assert.Throws<ObjectDisposedException>(window.Minimize);
        Assert.Throws<ObjectDisposedException>(window.Show);
        Assert.Throws<ObjectDisposedException>(() => window.IsVisible);
        Assert.Throws<ObjectDisposedException>(() => window.IsMinimized);
        Assert.Throws<ObjectDisposedException>(() => hud.HudLabelText);
        Assert.Throws<ObjectDisposedException>(() => hud.IsPixelInfoEnabled);
        Assert.Throws<ObjectDisposedException>(() => hud.IsPixelInfoEnabled = true);
        Assert.Throws<ObjectDisposedException>(() => queries.QueryOptions);
        Assert.Throws<ObjectDisposedException>(() => queries.QueryMetrics);
        Assert.Throws<ObjectDisposedException>(() => drawing.DrawLine(new(), new(1, 1), Brushes.Red));
        var releases = 0;
        var closed = await sink.SubmitFrameAsync(ImageFrame.TakeOwnership(
            new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 1 }, () => releases++));
        Assert.Equal(FrameSubmitStatus.Closed, closed.Status);
        Assert.Equal(1, releases);
    }

    [Fact]
    public void ViewerImplementationMatchesItsFacade()
    {
        var contract = typeof(IViewer).GetInterfaces().Append(typeof(IViewer))
            .SelectMany(type => type.GetMethods()).Select(m => m.ToString()).ToHashSet();
        foreach (var method in typeof(Viewer).GetMethods(System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            Assert.Contains(method.ToString(), contract);
    }

    [Fact]
    public async Task LayerTypesExposeOnlyTheirContentCapabilities()
    {
        await using var viewer = Create();
        Assert.IsType<DrawingLayer>(viewer.Layers.Markers);
        Assert.IsType<MeasurementLayer>(viewer.Layers.Measurements);
        Assert.Null(typeof(MeasurementLayer).GetMethod("Add"));
        Assert.Null(typeof(MeasurementLayer).GetEvent("DrawingClicked"));
        Assert.Empty(typeof(ViewerLayer).GetConstructors());
        Assert.Contains(viewer.Layers.Measurements, viewer.Layers.Items);
        Assert.Throws<InvalidOperationException>(() => viewer.Layers.RemoveLayer(viewer.Layers.Measurements));
        using var batch = viewer.Layers.Markers.Add([new CircleElement(new(), 2, Brushes.Red)]);
        viewer.Layers.Measurements.Clear();
        batch.Replace([new CircleElement(new(), 3, Brushes.Red)]);
        viewer.Layers.Measurements.IsVisible = false;
        Assert.True(viewer.Layers.Markers.IsVisible);
    }

    [Fact]
    public void OnlyCompleteFacadeExposesGlobalLayerManagement()
    {
        Assert.Equal(typeof(ViewerLayers), typeof(IViewer).GetProperty("Layers")!.PropertyType);
        foreach (var capability in typeof(IViewer).GetInterfaces())
        {
            Assert.Null(capability.GetProperty("Layers"));
            Assert.DoesNotContain(capability.GetMethods(), method => method.ReturnType == typeof(ViewerLayers));
        }
        Assert.Equal(typeof(DrawingLayer), typeof(IViewerDrawing).GetProperty("Markers")!.PropertyType);
        Assert.Equal(typeof(MeasurementLayer), typeof(IViewerMeasurements).GetProperty("Measurements")!.PropertyType);
    }

    [Fact]
    public async Task BorrowedLayerManagementKeepsOtherContentAndGlobalClearRemovesBoth()
    {
        await using var viewer = Create();
        IViewerDrawing drawing = viewer;
        IViewerMeasurements measurements = viewer;
        Assert.Same(viewer.Layers.Markers, drawing.Markers);
        Assert.Same(viewer.Layers.Measurements, measurements.Measurements);
        var completed = new List<IMeasurement>();
        var removed = new List<IMeasurement>();
        measurements.MeasurementCompleted += (_, e) => completed.Add(e.Measurement);
        measurements.MeasurementRemoved += (_, e) => removed.Add(e.Measurement);
        async Task CompletePoint() => await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            measurements.ActivateMeasurementTool(MeasurementToolIds.Point);
            viewer.Host.Interaction.ImageDown(2, 3);
        });

        await CompletePoint();
        using var firstDrawing = drawing.Markers.Add(new CircleElement(new(), 2, Brushes.Red));
        await Task.Run(drawing.Markers.Clear);
        Assert.Empty(removed);
        completed[0].UpdateGeometry(MeasurementGeometry.Point(new(4, 5)));
        Assert.Throws<ObjectDisposedException>(() => firstDrawing.Replace(new CircleElement(new(), 3, Brushes.Red)));

        using var secondDrawing = drawing.Markers.Add(new CircleElement(new(), 2, Brushes.Red));
        measurements.ActivateMeasurementTool(MeasurementToolIds.Length);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => viewer.Host.Interaction.ImageDown(1, 1));
        await Task.Run(measurements.Measurements.Clear);
        Assert.Equal(completed, removed);
        secondDrawing.Replace(new CircleElement(new(), 3, Brushes.Red));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => viewer.Host.Interaction.ImageDown(5, 5));
        Assert.Single(completed); // The cancelled preview must not complete on the next click.

        await CompletePoint();
        await Task.Run(((IViewer)viewer).Layers.ClearContents);
        Assert.Equal(completed, removed);
        Assert.Throws<ObjectDisposedException>(() => secondDrawing.Replace(new CircleElement(new(), 4, Brushes.Red)));
    }

    [Fact]
    public async Task MeasurementEventsExcludePreviewsAndCaptureLatestRemovalGeometry()
    {
        await using var viewer = Create();
        IViewerMeasurements measurements = viewer;
        var completed = new List<MeasurementEventArgs>();
        var removed = new List<MeasurementEventArgs>();
        measurements.MeasurementCompleted += (_, _) => throw new Exception("isolated subscriber");
        measurements.MeasurementCompleted += (_, e) => completed.Add(e);
        measurements.MeasurementRemoved += (_, e) => removed.Add(e);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            measurements.ActivateMeasurementTool(MeasurementToolIds.Length);
            viewer.Host.Interaction.ImageDown(1, 2);
            Assert.Empty(completed);
            measurements.EndInteraction();
            Assert.Empty(removed);
            measurements.ActivateMeasurementTool(MeasurementToolIds.Length);
            viewer.Host.Interaction.ImageDown(1, 2);
            viewer.Host.Interaction.ImageDown(5, 6);
            Assert.Single(completed);
            var item = viewer.Host.Window.MeasurementOverlay.Canvas.Children.OfType<System.Windows.Shapes.Line>()
                .Select(s => viewer.Host.Measurements.Find(s)).Single(i => i != null)!;
            item.UpdateGeometry(MeasurementGeometry.Line(new(3, 4), new(7, 8)));
        });
        Assert.Equal(new Point(5, 6), Assert.IsType<LineMeasurementGeometry>(completed[0].Snapshot.Geometry).End);
        await Task.Run(() => completed[0].Measurement.Dispose());
        Assert.Single(removed);
        Assert.Equal(completed[0].Snapshot.Id, removed[0].Snapshot.Id);
        Assert.Equal(new Point(7, 8), Assert.IsType<LineMeasurementGeometry>(removed[0].Snapshot.Geometry).End);
        completed[0].Measurement.Dispose();
        Assert.Single(removed);
        await viewer.DisposeAsync();
        completed[0].Measurement.Dispose();
    }

    [Fact]
    public async Task CompletionCanRemoveImmediatelyAndCloseRemovesCompletedItems()
    {
        await using var viewer = Create();
        int removals = 0;
        viewer.MeasurementRemoved += (_, _) => removals++;
        EventHandler<MeasurementEventArgs> remove = (_, e) => e.Measurement.Dispose();
        viewer.MeasurementCompleted += remove;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => { viewer.ActivateMeasurementTool(MeasurementToolIds.Point); viewer.Host.Interaction.ImageDown(2, 3); });
        Assert.Equal(1, removals);
        viewer.MeasurementCompleted -= remove;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => { viewer.ActivateMeasurementTool(MeasurementToolIds.Point); viewer.Host.Interaction.ImageDown(4, 5); });
        await viewer.DisposeAsync();
        Assert.Equal(2, removals);
    }
}
