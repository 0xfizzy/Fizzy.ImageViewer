namespace Fizzy.ImageViewer.Frames;

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
    /// <summary>Transfers an independently owned CPU image with the requested dimensions and
    /// the source pixel format. Returned pixels use region-local coordinates; the provider
    /// must not mutate or release them after returning. A local read must not implicitly
    /// download the full source image. Complete outstanding backend access before returning
    /// or throwing, including cancellation, so the caller can safely release the source lease.</summary>
    ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct);
}
