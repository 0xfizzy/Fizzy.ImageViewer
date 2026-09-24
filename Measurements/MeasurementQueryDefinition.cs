using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>The single boundary for supported query/geometry pairs and request construction.</summary>
internal static class MeasurementQueryDefinition
{
    private delegate QueryRequest? RequestFactory(MeasurementGeometry geometry, FrameDescriptor descriptor,
        QueryIdentity identity, Action<MeasurementQueryResult> publish);

    internal static void Validate(MeasurementQueryKind query, MeasurementGeometry geometry) => _ = Resolve(query, geometry);

    internal static QueryRequest? CreateRequest(MeasurementQueryKind query, MeasurementGeometry geometry,
        FrameDescriptor descriptor, QueryIdentity identity, Action<MeasurementQueryResult> publish)
        => Resolve(query, geometry)(geometry, descriptor, identity, publish);

    private static RequestFactory Resolve(MeasurementQueryKind query, MeasurementGeometry geometry) => (query, geometry) switch
    {
        (MeasurementQueryKind.None, _) => NoQuery,
        (MeasurementQueryKind.Pixel, PointMeasurementGeometry or CrosshairMeasurementGeometry) => Pixel,
        (MeasurementQueryKind.LineProfile, LineMeasurementGeometry) => LineProfile,
        (MeasurementQueryKind.RegionStatistics, RectangleMeasurementGeometry) => RegionStatistics,
        _ => throw new ArgumentException("The query must match the measurement geometry.", nameof(query))
    };

    private static QueryRequest? NoQuery(MeasurementGeometry geometry, FrameDescriptor descriptor,
        QueryIdentity identity, Action<MeasurementQueryResult> publish) => null;

    private static QueryRequest? Pixel(MeasurementGeometry geometry, FrameDescriptor descriptor,
        QueryIdentity identity, Action<MeasurementQueryResult> publish)
    {
        var x = Math.Floor(geometry.Anchor.X);
        var y = Math.Floor(geometry.Anchor.Y);
        if (x < 0 || y < 0 || x >= descriptor.Width || y >= descriptor.Height) return null;
        PixelCoordinate[] coordinates = [new((int)x, (int)y)];
        return new PixelQueryRequest(identity, coordinates, (frame, samples) =>
            publish(new MeasurementSampleResult(identity.ClientId, identity.GeometryVersion, frame,
                MeasurementQueryKind.Pixel, coordinates, samples.ToArray())));
    }

    private static QueryRequest? LineProfile(MeasurementGeometry geometry, FrameDescriptor descriptor,
        QueryIdentity identity, Action<MeasurementQueryResult> publish)
    {
        var line = (LineMeasurementGeometry)geometry;
        var coordinates = LineSampling.GetCoordinates(descriptor, line.Start.X, line.Start.Y, line.End.X, line.End.Y);
        if (coordinates.Length == 0) return null;
        return new LineProfileQueryRequest(identity, coordinates, (frame, samples) =>
            publish(new MeasurementSampleResult(identity.ClientId, identity.GeometryVersion, frame,
                MeasurementQueryKind.LineProfile, coordinates, samples.ToArray())));
    }

    private static QueryRequest? RegionStatistics(MeasurementGeometry geometry, FrameDescriptor descriptor,
        QueryIdentity identity, Action<MeasurementQueryResult> publish)
    {
        var region = ((RectangleMeasurementGeometry)geometry).ToRegion(descriptor);
        return region.IsEmpty ? null : new RegionStatisticsQueryRequest(identity, region,
            (frame, stats) => publish(new MeasurementRegionResult(identity.ClientId, identity.GeometryVersion,
                frame, region, stats.Channels.ToArray())));
    }
}
