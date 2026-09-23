using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

public readonly record struct ChannelStatistics(long Count, double? Minimum, double? Maximum, double? Mean);
public sealed record RegionStatistics(FramePixelFormat Format, ChannelStatistics[] Channels);
public sealed record PixelQueryResult(FrameInfo Frame, PixelSample[] Samples);
public sealed record RegionStatisticsResult(FrameInfo Frame, PixelRegion Region, RegionStatistics Statistics);
