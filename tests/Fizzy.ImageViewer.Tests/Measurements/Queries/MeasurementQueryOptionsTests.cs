using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class MeasurementQueryOptionsTests
{
    [Fact]
    public void PublicationsRetainIndependentSamplesAndImmutableCoordinateSnapshot()
    {
        var descriptor = new FrameDescriptor(4, 1, 4, FramePixelFormat.Gray8);
        var results = new List<MeasurementSampleResult>();
        var request = Assert.IsType<CoordinateQueryRequest>(MeasurementQueryDefinition.CreateRequest(
            MeasurementQueryKind.LineProfile, MeasurementGeometry.Line(new(0.5, 0.5), new(3.5, 0.5)),
            descriptor, new(Guid.NewGuid(), 0), result => results.Add(Assert.IsType<MeasurementSampleResult>(result))));
        var samples = Enumerable.Repeat(new PixelSample(FramePixelFormat.Gray8, 7, 0, 0, 0, 255), 4).ToArray();
        request.Publish(new(1, descriptor, null), samples);
        samples[0] = samples[0] with { Gray = 99 };
        request.Publish(new(2, descriptor, null), samples);
        Array.Clear(samples);
        request.Coordinates[0] = new(99, 99);
        Assert.Equal(7, results[0].Samples[0].Gray);
        Assert.Equal(99, results[1].Samples[0].Gray);
        Assert.Equal(new PixelCoordinate(0, 0), results[0].Coordinates[0]);
        Assert.Equal(results[0].Coordinates, results[1].Coordinates);
        Assert.Throws<NotSupportedException>(() => ((IList<PixelCoordinate>)results[0].Coordinates)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<PixelSample>)results[0].Samples)[0] = default);
    }

    [Fact]
    public void ProfileWindowRequiresAnExplicitLineQuery()
    {
        Assert.Equal(MeasurementQueryKind.None, new MeasurementOptions().Query);
        Assert.False(new MeasurementOptions().ShowProfileWindow);
        var line = MeasurementGeometry.Line(new(), new(1, 1));
        new MeasurementOptions { Query = MeasurementQueryKind.LineProfile, ShowProfileWindow = true }.Validate(line);
        foreach (var query in new[] { MeasurementQueryKind.None, MeasurementQueryKind.Pixel, MeasurementQueryKind.RegionStatistics })
            Assert.Throws<ArgumentException>(() => new MeasurementOptions { Query = query, ShowProfileWindow = true }.Validate(line));
        Assert.Throws<ArgumentException>(() => new MeasurementOptions { Query = (MeasurementQueryKind)999 }.Validate(line));
    }

    [Fact]
    public void GeometryValidationAndRequestsAgreeForEverySupportedPair()
    {
        var point = MeasurementGeometry.Point(new(1, 1));
        var crosshair = MeasurementGeometry.Crosshair(new(1, 1));
        var line = MeasurementGeometry.Line(new(), new(2, 0));
        var rectangle = MeasurementGeometry.Rectangle(new(), new(2, 2));
        var circle = MeasurementGeometry.Circle(new(1, 1), 1);
        var geometries = new MeasurementGeometry[] { point, crosshair, line, rectangle, circle };
        var descriptor = new FrameDescriptor(4, 4, 4, FramePixelFormat.Gray8);
        var identity = new QueryIdentity(Guid.NewGuid(), 7);
        foreach (var query in Enum.GetValues<MeasurementQueryKind>())
        foreach (var geometry in geometries)
        {
            bool supported = query switch
            {
                MeasurementQueryKind.None => true,
                MeasurementQueryKind.Pixel => geometry == point || geometry == crosshair,
                MeasurementQueryKind.LineProfile => geometry == line,
                MeasurementQueryKind.RegionStatistics => geometry == rectangle,
                _ => false
            };
            var options = new MeasurementOptions { Query = query };
            if (!supported)
            {
                Assert.Throws<ArgumentException>(() => options.Validate(geometry));
                Assert.Throws<ArgumentException>(() => MeasurementQueryDefinition.CreateRequest(query, geometry, descriptor, identity, _ => { }));
                continue;
            }
            options.Validate(geometry);
            MeasurementQueryResult? result = null;
            var request = MeasurementQueryDefinition.CreateRequest(query, geometry, descriptor, identity, value => result = value);
            var frame = new FrameInfo(3, descriptor, null);
            switch (query)
            {
                case MeasurementQueryKind.None:
                    Assert.Null(request);
                    continue;
                case MeasurementQueryKind.Pixel:
                    var pixel = Assert.IsType<CoordinateQueryRequest>(request);
                    Assert.Equal(QueryRateCategory.Pixel, pixel.RateCategory);
                    Assert.Equal(new PixelCoordinate(1, 1), Assert.Single(pixel.Coordinates));
                    pixel.Publish(frame, [new(FramePixelFormat.Gray8, 12, 0, 0, 0, 255)]);
                    Assert.Equal(12, Assert.Single(Assert.IsType<MeasurementSampleResult>(result).Samples).Gray);
                    break;
                case MeasurementQueryKind.LineProfile:
                    var profile = Assert.IsType<CoordinateQueryRequest>(request);
                    Assert.Equal(QueryRateCategory.Line, profile.RateCategory);
                    Assert.Equal(new PixelCoordinate[] { new(0, 0), new(1, 0), new(2, 0) }, profile.Coordinates);
                    profile.Publish(frame, new PixelSample[3]);
                    Assert.Equal(3, Assert.IsType<MeasurementSampleResult>(result).Samples.Count);
                    break;
                case MeasurementQueryKind.RegionStatistics:
                    var region = Assert.IsType<RegionStatisticsQueryRequest>(request);
                    Assert.Equal(QueryRateCategory.Region, region.RateCategory);
                    Assert.Equal(new PixelRegion(0, 0, 2, 2), region.Region);
                    region.Publish(frame, new RegionStatistics(FramePixelFormat.Gray8, [new(0, null, null, null)]));
                    Assert.Equal(region.Region, Assert.IsType<MeasurementRegionResult>(result).Region);
                    break;
            }
            Assert.Equal(query, result!.Query);
            Assert.Equal(identity.ClientId, result.MeasurementId);
            Assert.Equal(identity.GeometryVersion, result.GeometryVersion);
            Assert.Equal(frame, result.Frame);
        }
    }
}
