using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

/// <summary>All operations read original pixels. No operation may silently materialize the full image.</summary>
public interface IFramePixelSource
{
    ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct);
    ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct);
    ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct);
}
