using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Internal;
using Fizzy.ImageViewer.Snapshots;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class SnapshotCaptureTests
{
    [Fact]
    public async Task ExportsSerializeAndCancelledWaiterReleasesItsLease()
    {
        var capture = new SnapshotCapture();
        var source = new BlockingRegionSource();
        int released = 0, cancelledReleased = 0;
        using var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8),
            (nint)1, source, () => Interlocked.Increment(ref released));
        using var owner = new CommittedFrameLease(frame.Acquire(), null, 7);
        frame.Dispose();
        var first = capture.CaptureAsync(owner.Acquire(), SnapshotKind.Raw);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource();
        using var cancelledFrame = ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            new byte[] { 8 }, () => Interlocked.Increment(ref cancelledReleased));
        var cancelled = capture.CaptureAsync(new(cancelledFrame.Acquire(), null, 7), SnapshotKind.Raw, ct: stop.Token);
        cancelledFrame.Dispose();
        var second = capture.CaptureAsync(owner.Acquire(), SnapshotKind.Display);
        owner.Dispose();
        try
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, cancelledReleased);
            Assert.Equal(1, source.Calls);
            Assert.Equal(0, released);
            Assert.False(second.IsCompleted);
        }
        finally { source.Release.TrySetResult(); }
        using var raw = await first.WaitAsync(TimeSpan.FromSeconds(5));
        using var display = await second.WaitAsync(TimeSpan.FromSeconds(5));
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
    public async Task FailedReadReleasesExportGateAndSource()
    {
        var capture = new SnapshotCapture();
        var source = new BlockingRegionSource { FailFirst = true };
        source.Release.SetResult();
        int released = 0;
        using var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8),
            (nint)1, source, () => released++);
        using var owner = new CommittedFrameLease(frame.Acquire(), null, 0);
        frame.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => capture.CaptureAsync(owner.Acquire(), SnapshotKind.Raw));
        using var snapshot = await capture.CaptureAsync(owner.Acquire(), SnapshotKind.Raw).WaitAsync(TimeSpan.FromSeconds(5));
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
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct)
            => throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct)
            => throw new NotSupportedException();
        public async ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct)
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
