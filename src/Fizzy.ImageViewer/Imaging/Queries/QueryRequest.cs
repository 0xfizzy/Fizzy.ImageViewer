using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging.Queries;

internal abstract record QueryRequest(QueryIdentity Identity, QueryRateCategory RateCategory);

internal delegate void PublishSamples(FrameInfo frame, ReadOnlySpan<PixelSample> samples);

internal sealed record CoordinateQueryRequest(QueryRateCategory RateCategory, QueryIdentity Identity,
    PixelCoordinate[] Coordinates, PublishSamples Publish) : QueryRequest(Identity, RateCategory);

internal sealed record RegionStatisticsQueryRequest(QueryIdentity Identity, PixelRegion Region, Action<FrameInfo, RegionStatistics> Publish)
    : QueryRequest(Identity, QueryRateCategory.Region);
