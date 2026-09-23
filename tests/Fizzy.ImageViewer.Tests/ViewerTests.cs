using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[CollectionDefinition("Viewer", DisableParallelization = true)]
public class ViewerCollection { }

[Collection("Viewer")]
public class ViewerTests
{
    private static ImageFrame Frame(byte value, Action? release = null) => ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { value }, release ?? (() => { }));
    private static Viewer Create(ICpuImagePresenter? presenter = null) => new(NullLogger<Viewer>.Instance, presenter ?? new WriteableBitmapPresenter(), false);

    [Fact]
    public async Task SnapshotUsesCommittedRangeWhileRedrawIsWaiting()
    {
        var presenter = new ObservingPresenter();
        await using var viewer = Create(presenter);
        await viewer.SubmitFrameAsync(Frame(100));
        int notifications = 0;
        viewer.FrameCommitted += _ => notifications++;
        Task<ImageSnapshot>? capture = null;
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.DisplayRange = new(0, 100);
            // Redraw cannot commit while this dispatcher action is executing.
            capture = viewer.CaptureSnapshotAsync(SnapshotKind.Display);
        });
        using var snapshot = await capture!;
        using var pixels = snapshot.AcquirePixels();
        Assert.Equal(0, snapshot.DisplayVersion);
        Assert.Equal(100, pixels.CpuPixels.Span[0]);
        await presenter.Redrawn.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewer.UiDispatcher.InvokeAsync(() => { });
        using var redrawn = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
        using var mapped = redrawn.AcquirePixels();
        Assert.Equal(1, redrawn.DisplayVersion);
        Assert.Equal(snapshot.Frame, redrawn.Frame);
        Assert.Equal(255, mapped.CpuPixels.Span[0]);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task MenuLeaseOutlivesShutdownAndPresenterIsDisposedOnceOnSta()
    {
        var presenter = new ObservingPresenter();
        var viewer = Create(presenter);
        int released = 0;
        try
        {
            await viewer.SubmitFrameAsync(Frame(63, () => Interlocked.Increment(ref released)));
            await viewer.UiDispatcher.InvokeAsync(viewer.Freeze);
            using var target = viewer.AcquireMenuSnapshot();
            await viewer.DisposeAsync();
            await viewer.DisposeAsync();
            Assert.Equal(0, released);
            Assert.Equal(1, presenter.Disposals);
            Assert.Equal(ApartmentState.STA, presenter.PresentApartment);
            Assert.Equal(ApartmentState.STA, presenter.DisposeApartment);
            using var snapshot = await viewer.CaptureSnapshotAsync(target!.Acquire(includeRegion: true), SnapshotKind.Raw, default);
            using var pixels = snapshot.AcquirePixels();
            Assert.Equal(63, pixels.CpuPixels.Span[0]);
            target.Dispose();
            target.Dispose();
            Assert.Equal(1, released);
        }
        finally { await viewer.DisposeAsync(); }
    }

    [Fact]
    public async Task MenuCloseBeforeClickAndReopenRetainIndependentTargets()
    {
        await using var viewer = Create();
        await viewer.SubmitFrameAsync(Frame(17));
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.Freeze();
            viewer.Unfreeze();
            using var clickTarget = viewer.AcquireMenuSnapshot();
            Assert.Equal(17, clickTarget!.View.Frame.CpuPixels.Span[0]);
            viewer.Freeze(); // Old ContextIdle cleanup must not release this session.
        });
        await viewer.UiDispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        using var reopened = viewer.AcquireMenuSnapshot();
        Assert.Equal(17, reopened!.View.Frame.CpuPixels.Span[0]);
        Assert.Equal(FrameSubmitStatus.Frozen, (await viewer.SubmitFrameAsync(Frame(25))).Status);
        await viewer.UiDispatcher.InvokeAsync(viewer.Unfreeze);
        await viewer.SubmitFrameAsync(Frame(30));
        Assert.Equal(17, reopened.View.Frame.CpuPixels.Span[0]);
    }

    [Fact]
    public async Task CapturedSnapshotSurvivesCloseAndNewCapturesAreRejected()
    {
        var viewer = Create();
        try
        {
            await viewer.SubmitFrameAsync(Frame(81));
            var capture = viewer.CaptureSnapshotAsync(SnapshotKind.Raw);
            await viewer.DisposeAsync();
            using var snapshot = await capture;
            using var pixels = snapshot.AcquirePixels();
            Assert.Equal(81, pixels.CpuPixels.Span[0]);
            Assert.Throws<ObjectDisposedException>(() => { _ = viewer.CaptureSnapshotAsync(SnapshotKind.Raw); });
        }
        finally { await viewer.DisposeAsync(); }
    }

    [Fact]
    public async Task NotificationsReadNewFrameAndFailuresAreIsolated()
    {
        await using var viewer = Create();
        double observed = -1;
        viewer.FrameCommitted += _ => throw new InvalidOperationException("subscriber");
        viewer.FrameCommitted += info =>
        {
            using var current = viewer.AcquireCurrentFrame();
            Assert.Equal(info, current!.Info);
            FramePixelReader.Instance.TryRead(current, 0, 0, out var p); observed = p.Gray;
        };
        var result = await viewer.SubmitFrameAsync(Frame(42), new() { OnCommitted = _ => throw new Exception("callback") });
        Assert.Equal(FrameSubmitStatus.Committed, result.Status); Assert.Equal(42, observed);
        await viewer.SubmitFrameAsync(Frame(99)); Assert.Equal(99, observed);
    }

    [Fact]
    public async Task OnlyLatestWaitingFrameSurvives()
    {
        var presenter = new BlockingPresenter();
        await using var viewer = Create(presenter);
        int released = 0;
        var first = viewer.SubmitFrameAsync(Frame(1)).AsTask();
        await presenter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var replaced = viewer.SubmitFrameAsync(Frame(2, () => Interlocked.Increment(ref released))).AsTask();
            var latest = viewer.SubmitFrameAsync(Frame(3)).AsTask();
            Assert.Equal(FrameSubmitStatus.Superseded, (await replaced).Status);
            Assert.Equal(1, released);
            presenter.Release.Set();
            Assert.Equal(FrameSubmitStatus.Committed, (await first).Status);
            Assert.Equal(FrameSubmitStatus.Committed, (await latest).Status);
        }
        finally { presenter.Release.Set(); }
        using var current = viewer.AcquireCurrentFrame();
        Assert.Equal(3, current!.CpuPixels.Span[0]);
    }

    [Fact]
    public async Task FreezePinsMenuSnapshotAndRejectsFrames()
    {
        await using var viewer = Create();
        var first = await viewer.SubmitFrameAsync(Frame(17));
        await viewer.UiDispatcher.InvokeAsync(viewer.Freeze);
        int released = 0;
        Assert.Equal(FrameSubmitStatus.Frozen, (await viewer.SubmitFrameAsync(Frame(20, () => released++))).Status);
        Assert.Equal(1, released);
        using var target = viewer.AcquireMenuSnapshot();
        await viewer.UiDispatcher.InvokeAsync(viewer.Unfreeze);
        await viewer.SubmitFrameAsync(Frame(25));
        using var snapshot = await viewer.CaptureSnapshotAsync(target!.Acquire(includeRegion: true), SnapshotKind.Raw, default);
        using var pixels = snapshot.AcquirePixels();
        Assert.Equal(first.FrameId, snapshot.Frame.FrameId); Assert.Equal(17, pixels.CpuPixels.Span[0]);
    }

    [Fact]
    public async Task CancellationCloseAndFailureHaveTerminalResults()
    {
        var presenter = new FailingPresenter();
        var viewer = Create(presenter);
        int released = 0;
        try
        {
            await viewer.SubmitFrameAsync(Frame(1));
            presenter.Fail = true;
            var failed = await viewer.SubmitFrameAsync(Frame(2, () => released++));
            Assert.Equal(FrameSubmitStatus.Failed, failed.Status); Assert.NotNull(failed.Error);
            using (var current = viewer.AcquireCurrentFrame()) Assert.Equal(1, current!.CpuPixels.Span[0]);
            Assert.Equal(FrameSubmitStatus.Cancelled, (await viewer.SubmitFrameAsync(Frame(3, () => released++), ct: new CancellationToken(true))).Status);
            await viewer.DisposeAsync();
            Assert.Equal(FrameSubmitStatus.Closed, (await viewer.SubmitFrameAsync(Frame(4, () => released++))).Status);
            Assert.Equal(3, released);
        }
        finally { await viewer.DisposeAsync(); }
    }

    [Fact]
    public async Task SizeAndFormatChangesAndDisplayRangePreserveRawData()
    {
        await using var viewer = Create();
        await viewer.SubmitFrameAsync(Frame(1));
        using var input = ImageFrame.Copy(new(2, 1, 4, FramePixelFormat.Gray16), new byte[] { 0xe8, 3, 0xff, 0xff });
        viewer.DisplayRange = new(0, 1000);
        var result = await viewer.SubmitFrameAsync(input);
        using var display = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
        using var raw = await viewer.CaptureSnapshotAsync(SnapshotKind.Raw);
        using var dp = display.AcquirePixels(); using var rp = raw.AcquirePixels();
        Assert.Equal(result.FrameId, raw.Frame.FrameId);
        Assert.Equal(255, dp.CpuPixels.Span[0]); Assert.Equal(0xe8, rp.CpuPixels.Span[0]);
        await viewer.SubmitFrameAsync(Frame(7));
        Assert.Equal(0xe8, rp.CpuPixels.Span[0]);
    }

    [Fact]
    public async Task FreezeInvalidatesWorkAlreadyQueuedForDispatcher()
    {
        await using var viewer = Create();
        await viewer.SubmitFrameAsync(Frame(10));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = viewer.UiDispatcher.InvokeAsync(() => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(10)); });
        await entered.Task;
        int released = 0;
        try
        {
            var queued = viewer.SubmitFrameAsync(Frame(20, () => released++)).AsTask();
            var freeze = viewer.UiDispatcher.InvokeAsync(viewer.Freeze, System.Windows.Threading.DispatcherPriority.Send);
            release.Set();
            await freeze;
            Assert.Equal(FrameSubmitStatus.Frozen, (await queued).Status);
            Assert.Equal(1, released);
            using var current = viewer.AcquireCurrentFrame(); Assert.Equal(10, current!.CpuPixels.Span[0]);
        }
        finally { release.Set(); await blocker; }
    }

    [Fact]
    public async Task ConcurrentCloseCompletesEverySubmissionAndReleasesAllStorage()
    {
        var viewer = Create();
        int released = 0;
        var jobs = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
            await viewer.SubmitFrameAsync(Frame((byte)i, () => Interlocked.Increment(ref released))))).ToArray();
        await viewer.DisposeAsync();
        var results = await Task.WhenAll(jobs).WaitAsync(TimeSpan.FromSeconds(10));
        await viewer.DisposeAsync();
        Assert.Equal(100, released);
        Assert.DoesNotContain(results, r => r.Status == FrameSubmitStatus.Failed);
    }

    [Fact]
    public async Task ImageCoordinatesStayInSourcePixelsDespiteSourceDpi()
    {
        await using var viewer = Create();
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var layer = new Controls.ImageLayer();
            var bitmap = BitmapSource.Create(2, 1, 192, 192, PixelFormats.Gray8, null, new byte[] { 0, 255 }, 2);
            layer.SetImage(bitmap, 2, 1);
            layer.Measure(new System.Windows.Size(100, 100));
            layer.Arrange(new System.Windows.Rect(0, 0, 100, 100));
            layer.UpdateLayout(); layer.FitImageToContainer();
            Assert.Equal(50, layer.Scaler.ScaleX);
            var point = layer.ImageToContainer(new System.Windows.Point(1, 0));
            Assert.Equal(50, point.X, 6); Assert.Equal(25, point.Y, 6);
            var back = layer.ContainerToImage(point);
            Assert.Equal(1, back.X, 6); Assert.Equal(0, back.Y, 6);
        });
    }

    [Fact]
    public async Task MeasurementsRunOffStaAndPublishSourceFrame()
    {
        await using var viewer = Create();
        var measurement = new TestMeasurement();
        await viewer.UiDispatcher.InvokeAsync(() => viewer.MeasurementContext.Register(measurement));
        var result = await viewer.SubmitFrameAsync(Frame(71));
        var info = await measurement.Published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(info);
        Assert.Equal(71, measurement.Value);

        Assert.Equal(ApartmentState.STA, measurement.PublishApartment);
        await viewer.DisposeAsync(); Assert.False(measurement.Disposed); // Subscribers own their lifetime; the scheduler only revokes subscriptions.
    }

    [Fact]
    public async Task DisposeIgnoresClosingCancellationAndStopsApis()
    {
        var viewer = Create();
        try
        {
            await viewer.UiDispatcher.InvokeAsync(() => viewer.WindowForTests.Closing += (_, e) => e.Cancel = true);
            await viewer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<ObjectDisposedException>(() => viewer.Show());
            Assert.Throws<ObjectDisposedException>(() => viewer.Title = "closed");
            Assert.Throws<ObjectDisposedException>(() => viewer.Layers.CreateLayer("after-close"));
            Assert.Equal(FrameSubmitStatus.Closed, (await viewer.SubmitFrameAsync(Frame(8))).Status);
        }
        finally { await viewer.DisposeAsync(); }
    }

    private sealed class TestMeasurement : IFrameQueryClient
    {
        public double Value;
        public bool Disposed;
        public ApartmentState PublishApartment;
        public TaskCompletionSource<bool> Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QueryRequest? Capture(FrameDescriptor descriptor)=>new LineProfileQueryRequest(new(Guid.Empty, 0), [new(0,0)], samples=> {
            Value=samples![0].Gray;PublishApartment=Thread.CurrentThread.GetApartmentState();Published.TrySetResult(true);
        });
        public void ClearResult() { }        public void Dispose() => Disposed = true;
    }

    private sealed class ObservingPresenter : ICpuImagePresenter
    {
        private readonly WriteableBitmapPresenter _inner = new();
        private int _presentations;
        internal TaskCompletionSource Redrawn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals;
        internal ApartmentState PresentApartment, DisposeApartment;
        public ImageSource Present(DisplayBuffer pixels)
        {
            PresentApartment = Thread.CurrentThread.GetApartmentState();
            var source = _inner.Present(pixels);
            if (++_presentations == 2) Redrawn.TrySetResult();
            return source;
        }
        public void Dispose()
        {
            DisposeApartment = Thread.CurrentThread.GetApartmentState();
            Disposals++;
            _inner.Dispose();
        }
    }

    private sealed class BlockingPresenter : ICpuImagePresenter
    {
        private readonly WriteableBitmapPresenter _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public ImageSource Present(DisplayBuffer pixels)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return _inner.Present(pixels);
        }
        public void Dispose() { Release.Set(); _inner.Dispose(); }
    }
    private sealed class FailingPresenter : ICpuImagePresenter
    {
        private readonly WriteableBitmapPresenter _inner = new();
        public bool Fail;
        public ImageSource Present(DisplayBuffer pixels) => Fail ? throw new InvalidOperationException("injected") : _inner.Present(pixels);
        public void Dispose() => _inner.Dispose();
    }
}
