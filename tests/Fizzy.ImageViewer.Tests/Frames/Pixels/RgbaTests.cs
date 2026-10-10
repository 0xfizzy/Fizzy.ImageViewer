using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class RgbaTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(254)]
    [InlineData(255)]
    public async Task RgbaKeepsOriginalChannelsAndMatchesBgraDisplay(int alpha)
    {
        byte[] rgba = [91, 173, 239, (byte)alpha, 88, 77, 91, 173, 239, (byte)alpha];
        byte[] bgra = [239, 173, 91, (byte)alpha, 88, 77, 239, 173, 91, (byte)alpha];
        using var frame = ImageFrame.Copy(new(1, 2, 6, FramePixelFormat.Rgba32), rgba);
        using var reference = ImageFrame.Copy(new(1, 2, 6, FramePixelFormat.Bgra32), bgra);
        using var lease = frame.Acquire();
        using var other = reference.Acquire();
        var points = await lease.ReadPixelsAsync(new PixelCoordinate[] { new(0, 0), new(0, 1) });
        Assert.All(points.Samples, p => Assert.Equal(new PixelSample(FramePixelFormat.Rgba32, 0, 91, 173, 239, alpha), p));
        using var display = DisplayConverter.Convert(lease, null, default);
        using var normalized = DisplayConverter.Convert(other, null, default, true);
        Assert.Equal(FramePixelFormat.Pbgra32, display.Format);
        Assert.Equal(normalized.Bytes, display.Bytes);
        using var region = await lease.ReadRegionAsync(new(0, 1, 1, 1));
        using var pixels = region.AcquirePixels();
        Assert.Equal(new byte[] {91, 173, 239, (byte)alpha}, pixels.CpuPixels.ToArray());
    }
}
