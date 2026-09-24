using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class LineSamplingTests
{
    [Fact]
    public void ClippingPreservesDirectionAndIncludesBothEndpoints()
    {
        var descriptor = new FrameDescriptor(4, 4, 4, FramePixelFormat.Gray8);
        Assert.Equal(new PixelCoordinate[] { new(3, 3), new(2, 2), new(1, 1), new(0, 0) },
            LineSampling.GetCoordinates(descriptor, 100, 100, -100, -100));
        Assert.Equal(new PixelCoordinate[] { new(2, 1) },
            LineSampling.GetCoordinates(descriptor, 2, 1, 2, 1));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, -2, -2, -1, -1));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, double.NaN, 0, 1, 1));
    }
}
