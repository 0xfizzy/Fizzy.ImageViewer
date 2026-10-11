using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

/// <summary>Captures independently owned snapshots of the committed frame or a region.
/// At most two captures are admitted per Viewer, with one export read executing at a time.
/// Further calls throw InvalidOperationException before acquiring a frame lease.
/// Accepted calls capture the committed frame synchronously and may finish after Viewer closure.</summary>
public interface ISnapshotSource
{
    /// <summary>Captures the whole current committed frame. The caller owns and must dispose the returned snapshot.</summary>
    /// <exception cref="InvalidOperationException">No committed frame exists or the capture capacity is full.</exception>
    Task<ImageSnapshot> CaptureSnapshotAsync(
        SnapshotKind kind, CancellationToken cancellationToken = default);

    /// <summary>Captures a nonempty region wholly inside the current committed frame. The caller owns and must dispose the returned snapshot.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The region is empty or outside the frame.</exception>
    /// <exception cref="InvalidOperationException">No committed frame exists or the capture capacity is full.</exception>
    Task<ImageSnapshot> CaptureSnapshotAsync(
        SnapshotKind kind, PixelRegion region, CancellationToken cancellationToken = default);
}
