using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

public sealed record RegionStatisticsResult(FrameInfo Frame, PixelRegion Region, RegionStatistics Statistics);
