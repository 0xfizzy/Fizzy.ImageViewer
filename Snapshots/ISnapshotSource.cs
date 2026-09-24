using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

/// <summary>Captures independently owned snapshots of the committed frame or a region.</summary>
public interface ISnapshotSource
{
    Task<ImageSnapshot> CaptureSnapshotAsync(
        SnapshotKind kind, CancellationToken ct = default);

    Task<ImageSnapshot> CaptureSnapshotAsync(
        SnapshotKind kind, PixelRegion region, CancellationToken ct = default);
}
