using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class FrameMetadataTests
{
    [Fact]
    public async Task StandaloneQueriesDescribeTheirActualPixels()
    {
        using var image = ImageFrame.Copy(new(2, 2, 2, FramePixelFormat.Gray8), new byte[] { 1, 2, 3, 4 });
        using var lease = image.Acquire();
        var samples = await lease.ReadPixelsAsync(new[] { new PixelCoordinate(1, 1) });
        var stats = await lease.ComputeRegionStatisticsAsync(new(0, 0, 2, 2));
        Assert.Equal(0, lease.Info.FrameId);
        Assert.Equal(lease.Descriptor, lease.Info.Descriptor);
        Assert.Equal(lease.Info, samples.Frame);
        Assert.Equal(lease.Info, stats.Frame);
    }

    [Theory]
    [InlineData(SnapshotKind.Raw)]
    [InlineData(SnapshotKind.Display)]
    public async Task DerivedPixelsHaveTheirOwnMetadataAndExplicitSource(SnapshotKind kind)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.SubmitFrameAsync(ImageFrame.Copy(new(2, 2, 4, FramePixelFormat.Gray16), new byte[8]));
        using var source = viewer.AcquireCurrentFrame()!;
        using var region = await source.ReadRegionAsync(new(1, 1, 1, 1));
        using var regionPixels = region.AcquirePixels();
        Assert.Equal(source.Info, region.SourceFrame);
        Assert.Equal(regionPixels.Descriptor, regionPixels.Info.Descriptor);
        Assert.Equal(0, regionPixels.Info.FrameId);
        Assert.Equal(1, regionPixels.Info.Descriptor.Width);
        using var snapshot = await viewer.CaptureSnapshotAsync(kind, new PixelRegion(1, 1, 1, 1));
        using var pixels = snapshot.AcquirePixels();
        Assert.Equal(source.Info, snapshot.SourceFrame);
        Assert.Equal(pixels.Descriptor, pixels.Info.Descriptor);
        Assert.Equal(0, pixels.Info.FrameId);
        Assert.Equal(kind == SnapshotKind.Raw ? FramePixelFormat.Gray16 : FramePixelFormat.Pbgra32, pixels.Info.Descriptor.Format);
        var result = await pixels.ReadPixelsAsync(new[] { new PixelCoordinate(0, 0) });
        Assert.Equal(pixels.Descriptor.Format, result.Samples[0].Format);
        Assert.Equal(pixels.Info, result.Frame);
        using var nested = await pixels.ReadRegionAsync(new(0, 0, 1, 1));
        Assert.Equal(pixels.Info, nested.SourceFrame);
    }
}
