using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Snapshots;
using Fizzy.ImageViewer.Viewport;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class DisplayRangeUpdateTests
{
    [Fact]
    public async Task EmptyHiddenViewerConfiguresFutureSubmissionImmediately()
    {
        await using var viewer = new Viewer(showWindow: false);
        viewer.SetDisplayRange(new(0, 100));
        Assert.Equal(new GrayDisplayRange(0, 100), viewer.DisplayRange);
        Assert.Null(viewer.AcquireCurrentFrame());
    }

    [Fact]
    public async Task RedrawFailureIsObservableAndSnapshotKeepsCommittedRange()
    {
        var presenter = new FailingPresenter();
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, true);
        await viewer.SubmitAndDisplayAsync(Frame(100));
        presenter.Fail = true;
        var failed = new TaskCompletionSource<DisplayFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewer.DisplayFailed += (_, _) => throw new InvalidOperationException("subscriber");
        viewer.DisplayFailed += (sender, failure) => { Assert.Same(viewer, sender); failed.TrySetResult(failure); };
        viewer.SetDisplayRange(new(0, 100));
        var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("redraw failed", error.Error.Message);
        Assert.Equal(1, error.DisplayVersion);
        Assert.Equal(new GrayDisplayRange(0, 100), error.DisplayRange);
        await viewer.Host.Control.Runtime.Pipeline.Completion;
        Assert.Equal(2, presenter.Calls); // No automatic retry.
        using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
        using var pixels = snapshot.AcquirePixels();
        Assert.Equal(0, snapshot.DisplayVersion);
        Assert.Equal(100, pixels.CpuPixels.Span[0]);
    }

    [Fact]
    public async Task HiddenSettingsCoalesceAndResumeWithoutNewInput()
    {
        var presenter = new FailingPresenter();
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, true);
        await viewer.SubmitAndDisplayAsync(Frame(100));
        viewer.Hide();
        viewer.SetDisplayRange(new(0, 100));
        viewer.SetDisplayRange(new(0, 200));
        Assert.Equal(1, presenter.Calls);
        using (var previous = await viewer.CaptureSnapshotAsync(SnapshotKind.Display)) Assert.Equal(0, previous.DisplayVersion);
        viewer.Visible = true;
        await presenter.Second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewer.Host.Control.Runtime.Pipeline.Completion;
        using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
        Assert.Equal(2, snapshot.DisplayVersion);
        Assert.Equal(2, presenter.Calls);
    }

    [Fact]
    public async Task LatestSubmissionUsesLatestHiddenSettingWithoutDuplicateRedraw()
    {
        var presenter = new FailingPresenter();
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, true);
        await viewer.SubmitAndDisplayAsync(Frame(100));
        viewer.Hide();
        viewer.SetDisplayRange(new(0, 200));
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var receipt = await viewer.SubmitFrameAsync(Frame(50), new() { OnCommitted = _ => shown.TrySetResult() }, cancellation.Token);
        cancellation.Cancel();
        Assert.Equal(FrameSubmitStatus.Accepted, receipt.Status);
        viewer.Visible = true;
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewer.Host.Control.Runtime.Pipeline.Completion;
        Assert.Equal(2, presenter.Calls);
    }

    [Fact]
    public async Task ShutdownDoesNotWaitForHiddenDisplayConfiguration()
    {
        var viewer = new Viewer(showWindow: false);
        await viewer.SubmitFrameAsync(Frame(100));
        viewer.SetDisplayRange(new(0, 200));
        await viewer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CurrentGpuRangeRejectionIsSynchronousAndPreservesRequest()
    {
        await using var viewer = new Viewer(showWindow: false);
        viewer.SetDisplayRange(new(0, 100));
        using var frame = D3D9ImageFrame.TakeOwnership(new(1, 1, 4, FramePixelFormat.Bgra32), (nint)1,
            new UnexpectedPixelSource(), () => { });
        using var current = frame.Acquire();
        var field = typeof(FramePipeline).GetField("_currentFrame",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            // Never attach the sentinel native surface.
            field.SetValue(viewer.Host.Control.Runtime.Pipeline, current);
            try
            {
                Assert.Throws<NotSupportedException>(() => viewer.SetDisplayRange(new(0, 200)));
                Assert.Equal(new GrayDisplayRange(0, 100), viewer.DisplayRange);
            }
            finally { field.SetValue(viewer.Host.Control.Runtime.Pipeline, null); }
        });
    }

    [Fact]
    public async Task PendingGpuRangeFailureIsReportedOnlyWhenDisplayResumes()
    {
        await using var viewer = new Viewer(showWindow: false);
        viewer.SetDisplayRange(new(0, 100));
        var failed = new TaskCompletionSource<DisplayFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewer.DisplayFailed += (_, failure) => failed.TrySetResult(failure);
        int released = 0;
        var receipt = await viewer.SubmitFrameAsync(D3D9ImageFrame.TakeOwnership(
            new(1, 1, 4, FramePixelFormat.Bgra32), (nint)1, new UnexpectedPixelSource(), () => released++));
        Assert.Equal(FrameSubmitStatus.Accepted, receipt.Status);
        Assert.False(failed.Task.IsCompleted);
        Assert.Equal(0, released);
        viewer.Visible = true;
        var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(receipt.FrameId, failure.Frame.FrameId);
        Assert.IsType<NotSupportedException>(failure.Error);
        await viewer.Host.Control.Runtime.Pipeline.Completion;
        Assert.Equal(1, released);
        Assert.Null(viewer.AcquireCurrentFrame());
    }

    [Fact]
    public async Task FreezeDefersQueuedRedrawUntilMenuResumes()
    {
        var presenter = new FailingPresenter();
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, true);
        await viewer.SubmitAndDisplayAsync(Frame(100));
        using var queued = new ManualResetEventSlim();
        var dispatcher = viewer.Host.Window.Dispatcher;
        System.Windows.Threading.DispatcherHookEventHandler hook = (_, e) =>
        {
            if (e.Operation.Priority == System.Windows.Threading.DispatcherPriority.Render) queued.Set();
        };
        dispatcher.Hooks.OperationPosted += hook;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                viewer.SetDisplayRange(new(0, 200));
                Assert.True(queued.Wait(TimeSpan.FromSeconds(5)));
                viewer.Host.Control.Runtime.MenuSession.Open();
            });
            await viewer.Host.Control.Runtime.Pipeline.Completion;
            Assert.Equal(1, presenter.Calls);
            await dispatcher.InvokeAsync(viewer.Host.Control.Runtime.MenuSession.Close);
            await presenter.Second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await viewer.Host.Control.Runtime.Pipeline.Completion;
            using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
            Assert.Equal(1, snapshot.DisplayVersion);
        }
        finally { dispatcher.Hooks.OperationPosted -= hook; }
    }

    private static ImageFrame Frame(byte value) => ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { value });

    private sealed class UnexpectedPixelSource : IFramePixelSource
    {
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingPresenter : ICpuImagePresenter
    {
        private readonly WriteableBitmapPresenter _inner = new();
        internal bool Fail;
        internal int Calls;
        internal TaskCompletionSource Second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ImageSource Present(DisplayBuffer pixels)
        {
            if (++Calls == 2) Second.TrySetResult();
            return Fail ? throw new InvalidOperationException("redraw failed") : _inner.Present(pixels);
        }
        public void Dispose() => _inner.Dispose();
    }
}
