using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

/// <summary>All operations read original pixels. No operation may silently materialize the full image.
/// ReadPixelsAsync transfers its returned sample array; the provider must not reuse or mutate it.
/// Statistics are immutable snapshots and region reads transfer an independently owned image.</summary>
public interface IFramePixelSource
{
    ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct);
    /// <summary>Returns statistics in Gray, R/G/B, or R/G/B/A order, excluding padding.
    /// Preserve premultiplied values; ignore non-finite values, using zero count and null
    /// minimum/maximum/mean when no finite values remain. Format must match the source frame.</summary>
    ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct);
    ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct);
}
