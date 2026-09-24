using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging.Queries;

internal abstract record QueryRequest(QueryIdentity Identity);

internal delegate void PublishSamples(FrameInfo frame, ReadOnlySpan<PixelSample> samples);

internal sealed record PixelQueryRequest(QueryIdentity Identity, PixelCoordinate[] Coordinates, PublishSamples Publish) : QueryRequest(Identity);

internal sealed record LineProfileQueryRequest(QueryIdentity Identity, PixelCoordinate[] Coordinates, PublishSamples Publish) : QueryRequest(Identity);

internal sealed record RegionStatisticsQueryRequest(QueryIdentity Identity, PixelRegion Region, Action<FrameInfo, RegionStatistics> Publish) : QueryRequest(Identity);
