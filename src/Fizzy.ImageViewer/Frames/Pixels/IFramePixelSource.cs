namespace Fizzy.ImageViewer.Frames;

/// <summary>All operations read original pixels. No operation may silently materialize the full image.
/// ReadPixelsAsync transfers its returned sample array; the provider must not reuse or mutate it.
/// Statistics are immutable snapshots and region reads transfer an independently owned image.</summary>
/// <remarks>All methods can be called concurrently, including across methods and leases of the same
/// frame. Providers must support this concurrency or serialize their backend access themselves.
/// Each operation must finish all backend access, including native work and callbacks, before its
/// ValueTask completes successfully, faults or is canceled. Completion allows the caller to release
/// its source lease; requesting cancellation alone does not end that ownership requirement.</remarks>
public interface IFramePixelSource
{
    /// <summary>Returns one original-value sample per coordinate, in input order and in the source format. Transfers the returned array to the caller.</summary>
    ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken cancellationToken);
    /// <summary>Returns statistics in Gray, R/G/B, or R/G/B/A order, excluding padding.
    /// Preserve premultiplied values; ignore non-finite values, using zero count and null
    /// minimum/maximum/mean when no finite values remain. Format must match the source frame.</summary>
    ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken cancellationToken);
    /// <summary>Transfers an independently owned CPU image with the requested dimensions and
    /// the source pixel format. Returned pixels use region-local coordinates; the provider
    /// must not mutate or release them after returning. A local read must not implicitly
    /// download the full source image.</summary>
    ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken cancellationToken);
}
