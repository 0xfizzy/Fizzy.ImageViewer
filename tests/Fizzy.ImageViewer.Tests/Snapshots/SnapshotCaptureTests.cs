using Microsoft.Extensions.Logging.Abstractions;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Snapshots;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class SnapshotCaptureTests
{
    [Fact]
    public async Task SnapshotRegionIsFixedAndIndependent()
    {
        await using var viewer=new Viewer(NullLogger<Viewer>.Instance,new Rendering.WriteableBitmapPresenter(),false);
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(3,2,3,FramePixelFormat.Gray8),new byte[]{1,2,3,4,5,6}));
        var pending=viewer.CaptureSnapshotAsync(SnapshotKind.Raw,new PixelRegion(1,0,2,2));
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(1,1,1,FramePixelFormat.Gray8),new byte[]{9}));
        using var snapshot=await pending;using var pixels=snapshot.AcquirePixels();
        Assert.Equal(new byte[]{2,3,5,6},pixels.CpuPixels.ToArray());Assert.Equal(new PixelRegion(1,0,2,2),snapshot.Region);
    }

    [Fact]
    public async Task ExportsSerializeAndCancelledWaiterReleasesItsLease()
    {
        var capture = new SnapshotCapture();
        var source = new BlockingRegionSource();
        int released = 0, cancelledReleased = 0;
        using var frame = D3D9ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            (nint)1, source, () => Interlocked.Increment(ref released));
        using var owner = new CommittedViewLease(frame.Acquire(), null, 7);
        frame.Dispose();
        var first = capture.CaptureAsync(owner.Acquire, SnapshotKind.Raw);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource();
        using var cancelledFrame = ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            new byte[] { 8 }, () => Interlocked.Increment(ref cancelledReleased));
        var cancelled = capture.CaptureAsync(() => new(cancelledFrame.Acquire(), null, 7), SnapshotKind.Raw, cancellationToken: stop.Token);
        cancelledFrame.Dispose();
        Task<ImageSnapshot>? second = null;
        try
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, cancelledReleased);
            second = capture.CaptureAsync(owner.Acquire, SnapshotKind.Display);
            owner.Dispose();
            Assert.Equal(1, source.Calls);
            Assert.Equal(0, released);
            Assert.False(second.IsCompleted);
        }
        finally { source.Release.TrySetResult(); }
        using var raw = await first.WaitAsync(TimeSpan.FromSeconds(5));
        using var display = await second!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, source.Calls);
        Assert.Equal(1, source.MaxConcurrent);
        Assert.Equal(1, released);
        Assert.Equal(7, display.DisplayVersion);
        using var pixels = raw.AcquirePixels();
        Assert.Equal(42, pixels.CpuPixels.Span[0]);
        owner.Dispose();
        Assert.Equal(1, released);
    }

    [Fact]
    public async Task FullCapacityRejectsBeforeAcquisitionAndWaitingCaptureKeepsItsFrame()
    {
        var capture = new SnapshotCapture();
        var source = new BlockingRegionSource();
        using var frame = D3D9ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            (nint)1, source, () => { });
        using var owner = new CommittedViewLease(frame.Acquire(), null, 0);
        var first = capture.CaptureAsync(owner.Acquire, SnapshotKind.Raw);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int released = 0, rejectedAcquisitions = 0;
        using var waitingFrame = ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            new byte[] { 91 }, () => released++);
        var waiting = capture.CaptureAsync(() => new(waitingFrame.Acquire(), null, 3), SnapshotKind.Raw);
        waitingFrame.Dispose();
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = capture.CaptureAsync(() =>
                {
                    rejectedAcquisitions++;
                    return owner.Acquire();
                }, SnapshotKind.Raw);
            });
            Assert.Equal(0, rejectedAcquisitions);
            Assert.Equal(0, released);
            Assert.False(waiting.IsCompleted);
        }
        finally { source.Release.TrySetResult(); }
        using var firstSnapshot = await first.WaitAsync(TimeSpan.FromSeconds(5));
        using var waitingSnapshot = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        using var pixels = waitingSnapshot.AcquirePixels();
        Assert.Equal(91, pixels.CpuPixels.Span[0]);
        Assert.Equal(3, waitingSnapshot.DisplayVersion);
        Assert.Equal(1, released);
        using var next = await capture.CaptureAsync(owner.Acquire, SnapshotKind.Raw);
    }

    [Fact]
    public async Task AcquisitionFailureDoesNotConsumeCapacity()
    {
        var capture = new SnapshotCapture();
        for (int i = 0; i < 3; i++)
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = capture.CaptureAsync(() => throw new ObjectDisposedException("source"), SnapshotKind.Raw);
            });
        using var frame = ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 });
        using var snapshot = await capture.CaptureAsync(() => new(frame.Acquire(), null, 0), SnapshotKind.Raw);
        using var pixels = snapshot.AcquirePixels();
        Assert.Equal(42, pixels.CpuPixels.Span[0]);
    }

    [Fact]
    public async Task FailedReadReleasesExportGateAndSource()
    {
        var capture = new SnapshotCapture();
        var source = new BlockingRegionSource { FailFirst = true };
        source.Release.SetResult();
        int released = 0;
        using var frame = D3D9ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            (nint)1, source, () => released++);
        using var owner = new CommittedViewLease(frame.Acquire(), null, 0);
        frame.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => capture.CaptureAsync(owner.Acquire, SnapshotKind.Raw));
        using var snapshot = await capture.CaptureAsync(owner.Acquire, SnapshotKind.Raw).WaitAsync(TimeSpan.FromSeconds(5));
        owner.Dispose();
        Assert.Equal(1, released);
        Assert.Equal(2, source.Calls);
    }

    private sealed class BlockingRegionSource : IFramePixelSource
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FailFirst;
        internal int Calls, MaxConcurrent;
        private int _active;
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public async ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref Calls);
            var active = Interlocked.Increment(ref _active);
            Interlocked.Exchange(ref MaxConcurrent, Math.Max(active, MaxConcurrent));
            try
            {
                Entered.TrySetResult();
                await Release.Task;
                if (FailFirst && call == 1) throw new InvalidOperationException("read failure");
                return ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 });
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
