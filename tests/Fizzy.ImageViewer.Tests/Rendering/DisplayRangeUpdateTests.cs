using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class DisplayRangeUpdateTests
{
    [Fact]
    public async Task EmptyViewerConfiguresFutureSubmissionImmediately()
    {
        await using var viewer = new Viewer(showWindow: false);
        var update = viewer.SetDisplayRangeAsync(new(0, 100));
        Assert.True(update.IsCompletedSuccessfully);
        Assert.Equal(new GrayDisplayRange(0, 100), viewer.DisplayRange);
    }

    [Fact]
    public async Task RedrawFailureIsObservableAndSnapshotKeepsCommittedRange()
    {
        var presenter = new FailingPresenter();
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, false);
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 100 }));
        presenter.Fail = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.SetDisplayRangeAsync(new(0, 100)));
        Assert.Equal("redraw failed", error.Message);
        Assert.Equal(new GrayDisplayRange(0, 100), viewer.DisplayRange);
        using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
        using var pixels = snapshot.AcquirePixels();
        Assert.Equal(0, snapshot.DisplayVersion);
        Assert.Equal(100, pixels.CpuPixels.Span[0]);
    }

    [Fact]
    public async Task LaterSettingCancelsPendingUpdateAndLatestCommits()
    {
        await using var viewer = new Viewer(showWindow: false);
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 100 }));
        Task? first = null, second = null;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            first = viewer.SetDisplayRangeAsync(new(0, 100));
            second = viewer.SetDisplayRangeAsync(new(0, 200));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first!);
        await second!.WaitAsync(TimeSpan.FromSeconds(5));
        using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
        Assert.Equal(2, snapshot.DisplayVersion);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task QueuedSubmissionTerminatesUpdateWithoutDuplicateRedraw(bool fail, bool cancel)
    {
        var presenter = new FailingPresenter();
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, false);
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 100 }));
        using var cancellation = new CancellationTokenSource();
        Task update;
        Task<FrameSubmitResult> submission;
        lock (viewer.Host.Lifetime.Gate)
        {
            presenter.Fail = fail;
            update = viewer.SetDisplayRangeAsync(new(0, 200));
            submission = viewer.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 50 }), ct: cancellation.Token).AsTask();
            if (cancel) cancellation.Cancel();
        }
        var result = await submission;
        Assert.Equal(fail ? FrameSubmitStatus.Failed : cancel ? FrameSubmitStatus.Cancelled : FrameSubmitStatus.Committed, result.Status);
        if (fail) await Assert.ThrowsAsync<InvalidOperationException>(() => update);
        else await update.WaitAsync(TimeSpan.FromSeconds(5));
        await viewer.Host.Pipeline.Completion;
        Assert.Equal(2, presenter.Calls);
    }

    [Fact]
    public async Task ShutdownCancelsPendingDisplayUpdate()
    {
        var viewer = new Viewer(showWindow: false);
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 100 }));
        Task? update = null;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            update = viewer.SetDisplayRangeAsync(new(0, 200));
            _ = viewer.DisposeAsync();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update!);
        await viewer.DisposeAsync();
    }

    [Fact]
    public async Task CurrentGpuRangeRejectionIsSynchronousAndPreservesRequest()
    {
        await using var viewer = new Viewer(showWindow: false);
        await viewer.SetDisplayRangeAsync(new(0, 100));
        using var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 4, FramePixelFormat.Bgra32), (nint)1,
            new UnexpectedPixelSource(), () => { });
        using var current = frame.Acquire();
        // Install only the pipeline's retained lease; never bind the sentinel native surface.
        var field = typeof(FramePipeline).GetField("_currentFrame",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        lock (viewer.Host.Lifetime.Gate)
        {
            field.SetValue(viewer.Host.Pipeline, current);
            try
            {
                Assert.Throws<NotSupportedException>(() => { _ = viewer.SetDisplayRangeAsync(new(0, 200)); });
                Assert.Equal(new GrayDisplayRange(0, 100), viewer.DisplayRange);
            }
            finally { field.SetValue(viewer.Host.Pipeline, null); }
        }
    }

    private sealed class UnexpectedPixelSource : IFramePixelSource
    {
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FailingPresenter : ICpuImagePresenter
    {
        private readonly WriteableBitmapPresenter _inner = new();
        internal bool Fail;
        internal int Calls;
        public ImageSource Present(DisplayBuffer pixels)
        {
            Calls++;
            return Fail ? throw new InvalidOperationException("redraw failed") : _inner.Present(pixels);
        }
        public void Dispose() => _inner.Dispose();
    }
}
