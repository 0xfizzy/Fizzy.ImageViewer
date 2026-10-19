using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ImageViewerControlTests
{
    private static ImageFrame Frame(Action? release = null) => ImageFrame.TakeOwnership(
        new(8, 8, 8, FramePixelFormat.Gray8), Enumerable.Repeat((byte)42, 64).ToArray(), release ?? (() => { }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CommonCapabilitiesAndNotificationsWorkInBothHosts(bool standalone) => ControlTestDispatcher.Run(async () =>
    {
        await using IViewer api = standalone ? new Viewer(showWindow: false) : (ImageViewerControl)XamlReader.Parse(
            "<v:ImageViewerControl xmlns:v='clr-namespace:Fizzy.ImageViewer;assembly=Fizzy.ImageViewer' />");
        var control = api is Viewer viewer ? viewer.Host.Control : (ImageViewerControl)api;
        IViewerCapabilities borrowed = api;
        Assert.Same(api, borrowed);
        Assert.Same(api.Layers, borrowed.Layers);
        Assert.Same(api.WhenStopping, ((IViewerLifetime)borrowed).WhenStopping);
        Assert.Equal(!standalone, control.Dispatcher.CheckAccess());
        var order = new List<string>();
        api.FrameCommitted += (_, _) => order.Add("event");
        Assert.Equal(FrameSubmitStatus.Committed, (await api.SubmitFrameAsync(Frame(), new()
        {
            OnCommitted = _ => order.Add("callback")
        })).Status);
        Assert.Equal(new[] { "callback", "event" }, order);
        using var line = api.DrawLine(new(1, 1), new(4, 4), Brushes.Red);
        using var hud = api.DrawHudText("embedded", Brushes.White);
        api.HudLabelText = "test";
        api.IsPixelInfoEnabled = false;
        Assert.Equal("test", api.HudLabelText);
        using var snapshot = await api.CaptureSnapshotAsync(SnapshotKind.Raw);
        using var pixels = snapshot.AcquirePixels();
        Assert.Equal(42, pixels.CpuPixels.Span[0]);
        using var menu = api.RegisterMenuItem(new Menus.MenuItem("test", () => { }));
        int completed = 0, removed = 0;
        api.MeasurementCompleted += (_, _) => throw new InvalidOperationException("subscriber");
        api.MeasurementCompleted += (sender, _) => { Assert.Same(api, sender); completed++; };
        api.MeasurementRemoved += (sender, _) => { Assert.Same(api, sender); removed++; };
        await control.Dispatcher.InvokeAsync(() =>
        {
            Assert.NotNull(control.ContextMenu);
            api.ActivateMeasurementTool(MeasurementToolIds.Point);
            control.Runtime.Interaction.ImageDown(2, 3);
        });
        Assert.Equal(1, completed);
        await api.DisposeAsync();
        Assert.Equal(1, removed);
        Assert.False(Dispatcher.CurrentDispatcher.HasShutdownStarted);
    });

    [Fact]
    public Task TwoControlsUnloadReloadAndIndependentLeaseSurvive() => ControlTestDispatcher.Run(async () =>
    {
        await using var first = new ImageViewerControl();
        await using var second = new ImageViewerControl();
        var panel = new StackPanel();
        panel.Children.Add(first); panel.Children.Add(second);
        var window = new Window { Content = panel, Width = 400, Height = 300, ShowActivated = false };
        int released = 0;
        try
        {
            window.Show();
            await first.SubmitFrameAsync(Frame(() => released++));
            await second.SubmitFrameAsync(Frame());
            first.ActivateMeasurementTool(MeasurementToolIds.Point);
            first.Runtime.Interaction.ImageDown(2, 3);
            var count = first.MeasurementOverlay.Canvas.Children.Count;
            first.ActivateMeasurementTool(MeasurementToolIds.Length);
            first.Runtime.Interaction.ImageDown(1, 1);
            panel.Children.Remove(first);
            await first.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(first.WhenStopping.IsCompleted);
            Assert.Null(first.Runtime.Interaction.ActiveId);
            Assert.Equal(count, first.MeasurementOverlay.Canvas.Children.Count);
            panel.Children.Add(first);
            await first.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            using var lease = first.AcquireCurrentFrame();
            await first.DisposeAsync();
            Assert.Equal(0, released);
            Assert.Equal(42, lease!.CpuPixels.Span[0]);
            lease.Dispose();
            Assert.Equal(1, released);
            Assert.Equal(FrameSubmitStatus.Committed, (await second.SubmitFrameAsync(Frame())).Status);
            Assert.False(window.Dispatcher.HasShutdownStarted);
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); window.Close(); }
    });

    [Fact]
    public Task ReplacementContentIsRejectedWithoutLosingTheSurface() => ControlTestDispatcher.Run(async () =>
    {
        await using var control = new ImageViewerControl();
        var original = control.Content;
        Assert.Throws<InvalidOperationException>(() => control.Content = new Grid());
        Assert.Same(original, control.Content);
        Assert.Equal(FrameSubmitStatus.Committed, (await control.SubmitFrameAsync(Frame())).Status);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task PartialConstructionReleasesOnceAndPreservesBothErrors(int stage) => ControlTestDispatcher.Run(() =>
    {
        var presenter = new FailingPresenter();
        var original = new InvalidOperationException("construction");
        var failure = Assert.Throws<AggregateException>(() => new ImageViewerControl(NullLogger.Instance, presenter,
            checkpoint => { if ((int)checkpoint == stage) throw original; }));
        Assert.Contains(original, failure.Flatten().InnerExceptions);
        Assert.Contains(presenter.Error, failure.Flatten().InnerExceptions);
        Assert.Equal(1, presenter.Disposals);
        Assert.False(Dispatcher.CurrentDispatcher.HasShutdownStarted);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DisposalFailureIsSharedWithoutStoppingHost() => ControlTestDispatcher.Run(async () =>
    {
        var presenter = new FailingPresenter();
        var control = new ImageViewerControl(NullLogger.Instance, presenter, null);
        var first = control.DisposeAsync().AsTask();
        Assert.True(control.WhenStopping.IsCompletedSuccessfully);
        Assert.Same(first, control.DisposeAsync().AsTask());
        Assert.Same(presenter.Error, await Assert.ThrowsAsync<InvalidOperationException>(() => first));
        Assert.Equal(1, presenter.Disposals);
        Assert.Equal(7, await control.Dispatcher.InvokeAsync(() => 7));
        Assert.Equal(FrameSubmitStatus.Closed, (await control.SubmitFrameAsync(Frame())).Status);
    });

    [Fact]
    public Task ClosingWaitsForAcceptedSubmissionRelease() => ControlTestDispatcher.Run(async () =>
    {
        await using var control = new ImageViewerControl();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Task.Run(async () => await control.SubmitFrameAsync(Frame(() =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(10));
        }), cancellationToken: new CancellationToken(true)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var closing = control.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            release.Set();
            Assert.Equal(FrameSubmitStatus.Cancelled, (await pending).Status);
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); await pending; }
    });

    [Fact]
    public async Task PrematureDispatcherShutdownProducesObservableFailure()
    {
        ImageViewerControl? control = null;
        await ControlTestDispatcher.Run(() =>
        {
            control = new ImageViewerControl();
            return Task.CompletedTask;
        });
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => control!.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("dispatcher", failure.Message);
        Assert.True(control!.WhenStopping.IsCompletedSuccessfully);
    }

    [Fact]
    public Task DisposalDrainsQueriesWithoutBlockingHostOrReleasingTheirStorage() => ControlTestDispatcher.Run(async () =>
    {
        await using var control = new ImageViewerControl();
        var source = new DelayedPixelSource();
        int released = 0;
        var frame = new ImageFrame(new FrameStorage(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }, source, () => released++));
        await control.SubmitFrameAsync(frame);
        var query = new PixelClient();
        control.Runtime.Queries.Register(query);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var closing = control.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Equal(0, released);
            Assert.Equal(7, await control.Dispatcher.InvokeAsync(() => 7));
            source.Release.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(query.Published);
            Assert.Equal(1, released);
        }
        finally { source.Release.TrySetResult(); }
    });

    [Fact]
    public Task ReentrantDisposalWaitsForTerminalNotifications() => ControlTestDispatcher.Run(async () =>
    {
        var control = new ImageViewerControl();
        Task? closing = null;
        bool removed = false;
        control.MeasurementRemoved += (_, _) => removed = true;
        control.MeasurementCompleted += (_, _) =>
        {
            closing = control.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
        };
        control.ActivateMeasurementTool(MeasurementToolIds.Point);
        control.Runtime.Interaction.ImageDown(1, 1);
        await closing!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(removed);
        Assert.False(control.Dispatcher.HasShutdownStarted);
    });

    [Fact]
    public Task UnloadCleanupFailureIsRetainedForDisposalAndSessionEndsOnce() => ControlTestDispatcher.Run(async () =>
    {
        var control = new ImageViewerControl();
        var tool = new FailingUnloadTool();
        using var registration = control.RegisterMeasurementTool(tool);
        control.ActivateMeasurementTool(tool.Id);
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.Null(control.Runtime.Interaction.ActiveId);
        Assert.Equal(1, tool.Disposals);
        Assert.Equal(FrameSubmitStatus.Committed, (await control.SubmitFrameAsync(Frame())).Status);
        var failure = await Record.ExceptionAsync(() => control.DisposeAsync().AsTask());
        Assert.NotNull(failure);
        Assert.Contains<Exception>(tool.Error, failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { failure });
        Assert.Equal(1, tool.Disposals);
        Assert.False(control.Dispatcher.HasShutdownStarted);
    });

    private sealed class FailingUnloadTool : IMeasurementTool
    {
        internal readonly InvalidOperationException Error = new("unload cleanup");
        internal int Disposals;
        public string Id => "failing-unload";
        public string DisplayName => Id;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => new TestMeasurementSession(
            _ => false, _ => { }, () => throw Error, () => Disposals++);
    }

    private sealed class DelayedPixelSource : IFramePixelSource
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
            return Enumerable.Repeat(new PixelSample(FramePixelFormat.Gray8, 42, 0, 0, 0, 255), coordinates.Length).ToArray();
        }
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PixelClient : Imaging.Queries.IFrameQueryClient
    {
        internal bool Published;
        public Imaging.Queries.QueryRequest? Capture(FrameDescriptor descriptor) => new Imaging.Queries.CoordinateQueryRequest(
            Imaging.Queries.QueryRateCategory.Pixel, new(Guid.Empty, 0), [new(0, 0)], (_, _) => Published = true);
        public void ClearResult() { }
        public void Dispose() { }
    }

    private sealed class FailingPresenter : ICpuImagePresenter
    {
        internal readonly InvalidOperationException Error = new("presenter cleanup");
        internal int Disposals;
        public ImageSource Present(Imaging.DisplayBuffer pixels) => throw new NotSupportedException();
        public void Dispose() { Disposals++; throw Error; }
    }
}
