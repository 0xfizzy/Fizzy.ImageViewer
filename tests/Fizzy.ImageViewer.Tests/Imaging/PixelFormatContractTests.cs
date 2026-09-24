using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class PixelFormatContractTests
{
    [Theory]
    [InlineData(FramePixelFormat.Gray8, new byte[] { 17 }, new double[] { 17 })]
    [InlineData(FramePixelFormat.Gray16, new byte[] { 2, 1 }, new double[] { 258 })]
    [InlineData(FramePixelFormat.Gray32Float, new byte[] { 0, 0, 192, 63 }, new double[] { 1.5 })]
    [InlineData(FramePixelFormat.Rgb24, new byte[] { 10, 20, 30 }, new double[] { 10, 20, 30 })]
    [InlineData(FramePixelFormat.Bgr24, new byte[] { 30, 20, 10 }, new double[] { 10, 20, 30 })]
    [InlineData(FramePixelFormat.Bgr32, new byte[] { 30, 20, 10, 99 }, new double[] { 10, 20, 30 })]
    [InlineData(FramePixelFormat.Bgra32, new byte[] { 30, 20, 10, 128 }, new double[] { 10, 20, 30, 128 })]
    [InlineData(FramePixelFormat.Pbgra32, new byte[] { 30, 20, 10, 128 }, new double[] { 10, 20, 30, 128 })]
    public async Task StatisticsPreserveSemanticChannels(FramePixelFormat format, byte[] bytes, double[] expected)
    {
        Assert.Equal(bytes.Length, format.BytesPerPixel());
        using var image = ImageFrame.Copy(new(1, 1, bytes.Length, format), bytes);
        using var lease = image.Acquire();
        var result = await lease.ComputeRegionStatisticsAsync(new(0, 0, 1, 1));
        Assert.Equal(expected.Select(value => new ChannelStatistics(1, value, value, value)), result.Statistics.Channels);
    }

    [Theory]
    [InlineData(FramePixelFormat.Gray8, 3)]
    [InlineData(FramePixelFormat.Bgr32, 4)]
    [InlineData(FramePixelFormat.Bgra32, 3)]
    public void StatisticsRejectIncorrectChannelCounts(FramePixelFormat format, int count)
        => Assert.Throws<ArgumentException>(() => new RegionStatistics(format, new ChannelStatistics[count]));

    [Fact]
    public void UnknownFormatsAreRejected()
    {
        var format = (FramePixelFormat)12345;
        Assert.Throws<NotSupportedException>(() => format.BytesPerPixel());
        Assert.Throws<NotSupportedException>(() => new RegionStatistics(format, []));
        Assert.Throws<NotSupportedException>(() => new PixelSample(format, 0, 0, 0, 0, 0).IsGrayscale);
    }

    [Fact]
    public async Task ProviderStatisticsMustMatchSourceEvenWithTheSameChannelCount()
    {
        using var image = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8),
            (nint)1, new WrongFormatSource(), () => { });
        using var lease = image.Acquire();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lease.ComputeRegionStatisticsAsync(new(0, 0, 1, 1)).AsTask());
    }

    private sealed class WrongFormatSource : IFramePixelSource
    {
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct)
            => ValueTask.FromResult(new RegionStatistics(FramePixelFormat.Gray16, [new(1, 42, 42, 42)]));
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct)
            => throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
