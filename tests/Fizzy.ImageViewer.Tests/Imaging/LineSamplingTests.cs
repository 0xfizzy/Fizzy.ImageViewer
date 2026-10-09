using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class LineSamplingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FractionalCoordinatesCoverSingleRowAndColumnInBothDirections(bool vertical)
    {
        var descriptor = new FrameDescriptor(vertical ? 1 : 4, vertical ? 4 : 1,
            vertical ? 1 : 4, FramePixelFormat.Gray8);
        var expected = Enumerable.Range(0, 4).Select(i => vertical ? new PixelCoordinate(0, i) : new(i, 0)).ToArray();
        double endX = vertical ? 0.5 : 3.5, endY = vertical ? 3.5 : 0.5;
        Assert.Equal(expected, LineSampling.GetCoordinates(descriptor, 0.5, 0.5, endX, endY));
        Assert.Equal(expected.Reverse(), LineSampling.GetCoordinates(descriptor, endX, endY, 0.5, 0.5));
    }

    [Fact]
    public void EndpointsUsePixelCoverageAndExclusiveOuterEdges()
    {
        var descriptor = new FrameDescriptor(4, 4, 4, FramePixelFormat.Gray8);
        Assert.Equal(new PixelCoordinate[] { new(0, 3), new(1, 3), new(2, 3), new(3, 3) },
            LineSampling.GetCoordinates(descriptor, -1, 3.5, 5, 3.5));
        Assert.Equal(new PixelCoordinate[] { new(0, 0) },
            LineSampling.GetCoordinates(descriptor, 0.5, 0.5, 0.99, 0.99));
        Assert.Equal(new PixelCoordinate[] { new(3, 3) },
            LineSampling.GetCoordinates(descriptor, 3.9, 3.9, 3.9, 3.9));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, 0, 4, 3, 4));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, 4, 0, 4, 3));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, 3, 5, 5, 3));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, 5, 3, 3, 5));
        Assert.Equal(new PixelCoordinate[] { new(0, 0) },
            LineSampling.GetCoordinates(descriptor, -1, 1, 1, -1));
        Assert.Equal(new PixelCoordinate[] { new(3, 3) },
            LineSampling.GetCoordinates(descriptor, Math.BitDecrement(4.0), Math.BitDecrement(4.0),
                Math.BitDecrement(4.0), Math.BitDecrement(4.0)));
        Assert.Equal(new PixelCoordinate[] { new(3, 0), new(2, 0), new(1, 0), new(0, 0) },
            LineSampling.GetCoordinates(descriptor, 1e9, 0.5, -1e9, 0.5));
    }

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
