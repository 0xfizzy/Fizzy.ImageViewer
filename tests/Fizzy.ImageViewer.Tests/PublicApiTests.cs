using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Interfaces;
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
    public void WindowAndEditingImplementationsAreNotExported()
    {
        var exported = typeof(Viewer).Assembly.GetExportedTypes();
        Assert.DoesNotContain(exported, t => t.Name is "ViewerWindow" or "MenuManager" || t.Namespace == "Fizzy.ImageViewer.Editing"
            || t.Name is "ImageLayer" or "HudLayer" or "OverlayLayer" or "MeasurementItem" or "MeasurementGeometry"
            or "PointMeasure" or "LineMeasure" or "RectMeasure" or "LineStrengthMeasure"
            or "PointTool" or "LineTool" or "RectTool" or "LineStrengthTool"
            or "IMeasureTool" or "CustomMeasureTool" or "IMeasurementContext");
        var contract = typeof(IViewerAPI).GetMethods().Select(m => m.ToString()).ToHashSet();
        contract.UnionWith(typeof(IAsyncDisposable).GetMethods().Select(m => m.ToString()));
        foreach (var method in typeof(Viewer).GetMethods(System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            Assert.Contains(method.ToString(), contract);
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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.StartMeasure(MeasureToolIds.Length);
            viewer.Interaction.ImageDown(1, 2);
            Assert.Empty(completed);
            viewer.CancelMeasure();
            Assert.Empty(removed);
            viewer.StartMeasure(MeasureToolIds.Length);
            viewer.Interaction.ImageDown(1, 2);
            viewer.Interaction.ImageDown(5, 6);
            Assert.Single(completed);
            var item = viewer.WindowForTests.Layer1.Canvas.Children.OfType<System.Windows.Shapes.Line>()
                .Select(s => viewer.MeasurementContext.Find(s)).Single(i => i != null)!;
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
        await viewer.UiDispatcher.InvokeAsync(() => { viewer.StartMeasure(MeasureToolIds.Point); viewer.Interaction.ImageDown(2, 3); });
        Assert.Equal(1, removals);
        viewer.MeasurementCompleted -= remove;
        await viewer.UiDispatcher.InvokeAsync(() => { viewer.StartMeasure(MeasureToolIds.Point); viewer.Interaction.ImageDown(4, 5); });
        await viewer.DisposeAsync();
        Assert.Equal(2, removals);
    }
}
