using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

/// <summary>Captures independently owned snapshots of the committed frame or a region.
/// At most two captures are admitted per Viewer, with one export read executing at a time.
/// Further calls throw InvalidOperationException before acquiring a frame lease.
/// Accepted calls capture the committed frame synchronously and may finish after Viewer closure.</summary>
public interface ISnapshotSource
{
    Task<ImageSnapshot> CaptureSnapshotAsync(
        SnapshotKind kind, CancellationToken ct = default);

    Task<ImageSnapshot> CaptureSnapshotAsync(
        SnapshotKind kind, PixelRegion region, CancellationToken ct = default);
}
