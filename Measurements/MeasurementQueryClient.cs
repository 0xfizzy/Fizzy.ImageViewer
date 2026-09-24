using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Builds and caches pixel requests; each request captures immutable geometry and result provenance.</summary>
internal sealed class MeasurementQueryClient(MeasurementItem item, MeasurementQuery query) : IFrameQueryClient
{
    private QueryRequest? _cached;
    private (long Version, FrameDescriptor Descriptor)? _cacheKey;

    public void ClearResult() => item.ClearResult();

    internal void Reset()
    {
        _cached = null;
        _cacheKey = null;
    }

    public QueryRequest? Capture(FrameDescriptor descriptor)
    {
        if (item.IsDisposed || !item.IsComplete || query == MeasurementQuery.None) return null;
        var key = (item.GeometryVersion, descriptor);
        if (_cacheKey == key) return _cached;
        var request = CreateRequest(descriptor);
        _cacheKey = key;
        return _cached = request;
    }

    private QueryRequest? CreateRequest(FrameDescriptor descriptor)
    {
        var identity = new QueryIdentity(item.Id, item.GeometryVersion);
        var geometry = item.Geometry;
        if (query == MeasurementQuery.RegionStatistics)
        {
            var region = ((RectangleMeasurementGeometry)geometry).ToRegion(descriptor);
            return region.IsEmpty ? null : new RegionStatisticsQueryRequest(identity, region,
                (frame, stats) => item.PublishResult(new(item.Id, identity.GeometryVersion, frame, query,
                    [], [], region, stats.Channels.ToArray())));
        }

        PixelCoordinate[] coordinates;
        if (query == MeasurementQuery.Pixel)
        {
            var x = Math.Floor(geometry.Anchor.X);
            var y = Math.Floor(geometry.Anchor.Y);
            if (x < 0 || y < 0 || x >= descriptor.Width || y >= descriptor.Height) return null;
            coordinates = [new((int)x, (int)y)];
        }
        else
        {
            var line = (LineMeasurementGeometry)geometry;
            coordinates = LineSampling.GetCoordinates(descriptor, line.Start.X, line.Start.Y, line.End.X, line.End.Y);
            if (coordinates.Length == 0) return null;
        }

        void Publish(FrameInfo frame, ReadOnlySpan<PixelSample> samples) =>
            item.PublishResult(new(item.Id, identity.GeometryVersion, frame, query, coordinates, samples.ToArray(), null, []));
        return query == MeasurementQuery.Pixel
            ? new PixelQueryRequest(identity, coordinates, Publish)
            : new LineProfileQueryRequest(identity, coordinates, Publish);
    }
}
