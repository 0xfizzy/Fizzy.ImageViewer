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
        IViewerAPI api = viewer;
        await Task.Run(async () =>
        {
            api.Label = "camera";
            Assert.Equal("camera", api.Label);
            api.QueryOptions = new();
            Assert.NotNull(api.QueryOptions);
            _ = api.QueryMetrics;
            api.Show();
            Assert.True(api.IsVisible);
            api.Minimize();
            Assert.True(api.IsMinimized);
            Assert.True(api.IsVisible);
            api.Show();
            Assert.False(api.IsMinimized);
            api.Hide();
            Assert.False(api.IsVisible);
            var result = await api.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }));
            Assert.Equal(FrameSubmitStatus.Committed, result.Status);
            using var line = api.DrawLine(new(0, 0), new(1, 1), Brushes.Red);
            api.Show();
            using var lease = api.AcquireCurrentFrame();
            Assert.Equal(42, lease!.CpuPixels.Span[0]);
        });
        await api.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(api.Hide);
        Assert.Throws<ObjectDisposedException>(api.Minimize);
        Assert.Throws<ObjectDisposedException>(api.Show);
        Assert.Throws<ObjectDisposedException>(() => api.IsVisible);
        Assert.Throws<ObjectDisposedException>(() => api.IsMinimized);
        Assert.Throws<ObjectDisposedException>(() => api.Label);
        Assert.Throws<ObjectDisposedException>(() => api.QueryOptions);
        Assert.Throws<ObjectDisposedException>(() => api.QueryMetrics);
        Assert.Throws<ObjectDisposedException>(() => api.DrawLine(new(), new(1, 1), Brushes.Red));
    }

    [Fact]
    public void ViewerImplementationMatchesItsFacade()
    {
        var contract = typeof(IViewerAPI).GetMethods().Select(m => m.ToString()).ToHashSet();
        contract.UnionWith(typeof(IAsyncDisposable).GetMethods().Select(m => m.ToString()));
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
        Assert.Null(typeof(MeasurementLayer).GetMethod("AddBatch"));
        Assert.Null(typeof(MeasurementLayer).GetEvent("BatchClicked"));
        Assert.Empty(typeof(ViewerLayer).GetConstructors());
        Assert.Contains(viewer.Layers.Measurements, viewer.Layers.Items);
        Assert.Throws<InvalidOperationException>(() => viewer.Layers.RemoveLayer(viewer.Layers.Measurements));
        using var batch = viewer.Layers.Markers.AddBatch([new CircleElement(new(), 2, Brushes.Red)]);
        viewer.Layers.Measurements.Clear();
        batch.Replace([new CircleElement(new(), 3, Brushes.Red)]);
        viewer.Layers.Measurements.IsVisible = false;
        Assert.True(viewer.Layers.Markers.IsVisible);
    }

    [Fact]
    public async Task MeasurementEventsExcludePreviewsAndCaptureLatestRemovalGeometry()
    {
        await using var viewer = Create();
        var completed = new List<MeasurementEventArgs>();
        var removed = new List<MeasurementEventArgs>();
        viewer.MeasurementCompleted += (_, _) => throw new Exception("isolated subscriber");
        viewer.MeasurementCompleted += (_, e) => completed.Add(e);
        viewer.MeasurementRemoved += (_, e) => removed.Add(e);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.StartMeasurement(MeasurementToolIds.Length);
            viewer.Host.Interaction.ImageDown(1, 2);
            Assert.Empty(completed);
            viewer.CancelMeasurement();
            Assert.Empty(removed);
            viewer.StartMeasurement(MeasurementToolIds.Length);
            viewer.Host.Interaction.ImageDown(1, 2);
            viewer.Host.Interaction.ImageDown(5, 6);
            Assert.Single(completed);
            var item = viewer.Host.Window.MeasurementOverlay.Canvas.Children.OfType<System.Windows.Shapes.Line>()
                .Select(s => viewer.Host.Measurements.Find(s)).Single(i => i != null)!;
            item.UpdateGeometry(MeasurementGeometry.Line(new(3, 4), new(7, 8)));
        });
        Assert.Equal(new Point(5, 6), completed[0].Snapshot.End);
        await Task.Run(() => completed[0].Handle.Dispose());
        Assert.Single(removed);
        Assert.Equal(completed[0].Snapshot.Id, removed[0].Snapshot.Id);
        Assert.Equal(new Point(7, 8), removed[0].Snapshot.End);
        completed[0].Handle.Dispose();
        Assert.Single(removed);
        await viewer.DisposeAsync();
        completed[0].Handle.Dispose();
    }

    [Fact]
    public async Task CompletionCanRemoveImmediatelyAndCloseRemovesCompletedItems()
    {
        await using var viewer = Create();
        int removals = 0;
        viewer.MeasurementRemoved += (_, _) => removals++;
        EventHandler<MeasurementEventArgs> remove = (_, e) => e.Handle.Dispose();
        viewer.MeasurementCompleted += remove;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => { viewer.StartMeasurement(MeasurementToolIds.Point); viewer.Host.Interaction.ImageDown(2, 3); });
        Assert.Equal(1, removals);
        viewer.MeasurementCompleted -= remove;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => { viewer.StartMeasurement(MeasurementToolIds.Point); viewer.Host.Interaction.ImageDown(4, 5); });
        await viewer.DisposeAsync();
        Assert.Equal(2, removals);
    }
}
