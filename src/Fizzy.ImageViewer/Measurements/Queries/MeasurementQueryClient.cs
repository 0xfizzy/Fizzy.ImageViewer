using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging.Queries;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Caches requests for a measurement's geometry and publishes their immutable query results.</summary>
internal sealed class MeasurementQueryClient(MeasurementItem item, MeasurementQueryKind query) : IFrameQueryClient
{
    private QueryRequest? _cached;
    private (long Version, FrameDescriptor Descriptor)? _cacheKey;

    public void ClearResult() => item.ClearQueryResult();

    internal void Reset()
    {
        _cached = null;
        _cacheKey = null;
    }

    public QueryRequest? Capture(FrameDescriptor descriptor)
    {
        if (item.IsDisposed || !item.IsComplete || query == MeasurementQueryKind.None) return null;
        var key = (item.GeometryVersion, descriptor);
        if (_cacheKey == key) return _cached;
        var request = MeasurementQueryDefinition.CreateRequest(query, item.Geometry, descriptor,
            new QueryIdentity(item.Id, item.GeometryVersion), item.PublishQueryResult);
        _cacheKey = key;
        return _cached = request;
    }
}
