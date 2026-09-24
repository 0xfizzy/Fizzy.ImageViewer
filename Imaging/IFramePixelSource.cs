using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

/// <summary>All operations read original pixels. No operation may silently materialize the full image.
/// ReadPixelsAsync transfers its returned sample array; the provider must not reuse or mutate it.
/// Statistics are immutable snapshots and region reads transfer an independently owned image.</summary>
public interface IFramePixelSource
{
    ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct);
    ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct);
    ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct);
}
