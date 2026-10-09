using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class LineSamplingTests
{
    [Theory]
    [InlineData(4294967295d)]
    [InlineData(4294967296d)]
    [InlineData(1e20)]
    [InlineData(double.MaxValue)]
    public void ExtremeClippingPreservesRowsColumnsAndDiagonalsInBothDirections(double extent)
    {
        var descriptor = new FrameDescriptor(10, 10, 10, FramePixelFormat.Gray8);
        Check(-extent, 5, extent, 5, Enumerable.Range(0, 10).Select(i => new PixelCoordinate(i, 5)));
        Check(5, -extent, 5, extent, Enumerable.Range(0, 10).Select(i => new PixelCoordinate(5, i)));
        Check(-extent, -extent, extent, extent, Enumerable.Range(0, 10).Select(i => new PixelCoordinate(i, i)));
        Check(-extent, extent, extent, -extent, [new PixelCoordinate(0, 0)]);
        Assert.Empty(LineSampling.GetCoordinates(descriptor, -extent, 10, extent, 10));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, 10, -extent, 10, extent));

        void Check(double x0, double y0, double x1, double y1, IEnumerable<PixelCoordinate> expected)
        {
            Assert.Equal(expected, LineSampling.GetCoordinates(descriptor, x0, y0, x1, y1));
            Assert.Equal(expected.Reverse(), LineSampling.GetCoordinates(descriptor, x1, y1, x0, y0));
        }
    }

    [Fact]
    public void ExactClippingRejectsExclusiveCornerOnlyAndOutsideSegments()
    {
        var descriptor = new FrameDescriptor(10, 10, 10, FramePixelFormat.Gray8);
        double extent = 4294967296d;
        Assert.Empty(LineSampling.GetCoordinates(descriptor, -extent, extent + 20, extent, -extent + 20));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, extent, -extent + 20, -extent, extent + 20));
        Assert.Empty(LineSampling.GetCoordinates(descriptor, -extent, -extent, -extent / 2, -extent / 2));
    }

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
