using Fizzy.ImageViewer.Measurements;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class MeasurementGeometryTests
{
    [Fact]
    public void BoundsNormalizeLinesWithoutChangingEndpointOrder()
    {
        var line = MeasurementGeometry.Line(new(8, 9), new(2, 3));
        Assert.Equal(new Rect(2, 3, 6, 6), line.Bounds);
        Assert.Equal(new Point(8, 9), line.Start);
        Assert.Equal(new Point(2, 3), line.End);
        Assert.Equal(line.Bounds, MeasurementGeometry.Rectangle(line.Start, line.End).Bounds);
    }

    [Fact]
    public void BoundsRepresentCircleDiameterAndZeroExtentTargets()
    {
        var center = new Point(3, 4);
        Assert.Equal(new Rect(1, 2, 4, 4), MeasurementGeometry.Circle(center, 2).Bounds);
        var zero = new Rect(center, center);
        Assert.Equal(zero, MeasurementGeometry.Circle(center, 0).Bounds);
        Assert.Equal(zero, MeasurementGeometry.Point(center).Bounds);
        Assert.Equal(zero, MeasurementGeometry.Crosshair(center).Bounds);
        Assert.Equal(zero, MeasurementGeometry.Rectangle(center, center).Bounds);
        Assert.Equal(zero, MeasurementGeometry.Line(center, center).Bounds);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GeometryRejectsNonFiniteCoordinatesAndRadius(double value)
    {
        var invalid = new Point(value, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Point(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Crosshair(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Line(new(), invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Rectangle(new(), invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(invalid, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(new(), value));
    }

    [Fact]
    public void GeometryRejectsNegativeRadiusAndOverflowingBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(new(), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(new(), double.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Circle(new(double.MaxValue, 0), double.MaxValue / 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Line(new(-double.MaxValue, 0), new(double.MaxValue, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementGeometry.Rectangle(new(-double.MaxValue, 0), new(double.MaxValue, 0)));
    }
}
