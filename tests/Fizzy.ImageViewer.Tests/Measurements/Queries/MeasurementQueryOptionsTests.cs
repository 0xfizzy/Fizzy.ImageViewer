using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class MeasurementQueryOptionsTests
{
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
                    var pixel = Assert.IsType<PixelQueryRequest>(request);
                    Assert.Equal(new PixelCoordinate(1, 1), Assert.Single(pixel.Coordinates));
                    pixel.Publish(frame, [new(FramePixelFormat.Gray8, 12, 0, 0, 0, 255)]);
                    Assert.Equal(12, Assert.Single(Assert.IsType<MeasurementSampleResult>(result).Samples).Gray);
                    break;
                case MeasurementQueryKind.LineProfile:
                    var profile = Assert.IsType<LineProfileQueryRequest>(request);
                    Assert.Equal(new PixelCoordinate[] { new(0, 0), new(1, 0), new(2, 0) }, profile.Coordinates);
                    profile.Publish(frame, new PixelSample[3]);
                    Assert.Equal(3, Assert.IsType<MeasurementSampleResult>(result).Samples.Count);
                    break;
                case MeasurementQueryKind.RegionStatistics:
                    var region = Assert.IsType<RegionStatisticsQueryRequest>(request);
                    Assert.Equal(new PixelRegion(0, 0, 2, 2), region.Region);
                    region.Publish(frame, new RegionStatistics(FramePixelFormat.Gray8, []));
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
