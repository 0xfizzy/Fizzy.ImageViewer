using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Snapshots;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class SnapshotOwnershipTests
{
    [Theory]
    [InlineData(SnapshotKind.Raw)]
    [InlineData(SnapshotKind.Display)]
    public async Task SourceReleaseFailureCleansUndeliveredSnapshot(SnapshotKind kind)
    {
        var source = new RegionSource();
        var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8), 1, source,
            () => throw new InvalidOperationException("source release"));
        var view = new CommittedViewLease(frame.Acquire(), null, 0);
        frame.Dispose();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SnapshotCapture().CaptureAsync(view, kind));
        Assert.Equal("source release", error.Message);
        Assert.Equal(1, source.Releases);
    }

    [Fact]
    public async Task RawSnapshotRetainsTransferredRegionUntilFinalLeaseRelease()
    {
        var source = new RegionSource();
        using var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8), 1, source, () => { });
        var snapshot = await new SnapshotCapture().CaptureAsync(new(frame.Acquire(), null, 0), SnapshotKind.Raw);
        Assert.Equal(0, source.Releases);
        using var pixels = snapshot.AcquirePixels();
        snapshot.Dispose();
        Assert.Equal(0, source.Releases);
        Assert.Equal(42, pixels.CpuPixels.Span[0]);
        pixels.Dispose();
        Assert.Equal(1, source.Releases);
    }

    private sealed class RegionSource : IFramePixelSource
    {
        internal int Releases;
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct) =>
            ValueTask.FromResult(ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }, () => Releases++));
    }
}
