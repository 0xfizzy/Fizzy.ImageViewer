using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Imaging.Queries;

internal sealed class QuerySubscription(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

internal readonly record struct QueryIdentity(Guid ClientId, long GeometryVersion, long SessionVersion = 0);
internal enum ResultInvalidation { CoordinatesChanged, DescriptorChanged, NoTarget, NoFrame, Expired, Failed }
internal readonly record struct QueryPolicy(bool AllowMovingResult = false, double? MaximumRate = null, TimeSpan? DisplayAge = null);
internal abstract record QueryRequest(QueryIdentity Identity);
internal delegate void PublishSamples(ReadOnlySpan<PixelSample> samples);
internal sealed record PixelQueryRequest(QueryIdentity Identity, PixelCoordinate[] Coordinates, PublishSamples Publish) : QueryRequest(Identity);
internal sealed record LineProfileQueryRequest(QueryIdentity Identity, PixelCoordinate[] Coordinates, PublishSamples Publish) : QueryRequest(Identity);
internal sealed record RegionStatisticsQueryRequest(QueryIdentity Identity, PixelRegion Region, Action<RegionStatistics> Publish) : QueryRequest(Identity);
internal abstract record QueryResult;
internal sealed record SamplesResult(ReadOnlyMemory<PixelSample> Samples) : QueryResult;
internal sealed record StatisticsResult(RegionStatistics Statistics) : QueryResult;
internal sealed record FailedQueryResult : QueryResult;

internal interface IFrameQueryClient
{
    QueryPolicy Policy => default;
    QueryRequest? Capture(FrameDescriptor descriptor);
    void ClearResult();
    void InvalidateResult(ResultInvalidation reason) => ClearResult();
    void ResultPublished(long frameId) { }
}
