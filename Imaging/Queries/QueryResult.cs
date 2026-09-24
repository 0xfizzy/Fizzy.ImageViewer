using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging.Queries;

internal abstract record QueryResult;

internal sealed record SamplesResult(ReadOnlyMemory<PixelSample> Samples) : QueryResult;

internal sealed record StatisticsResult(RegionStatistics Statistics) : QueryResult;

internal sealed record FailedQueryResult : QueryResult;
