namespace Fizzy.ImageViewer.Frames;

public sealed record RegionStatisticsResult(FrameInfo Frame, PixelRegion Region, RegionStatistics Statistics);
