using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <inheritdoc/>
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken cancellationToken = default)
        => CaptureSnapshot(kind, null, cancellationToken);

    /// <inheritdoc/>
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken cancellationToken = default)
        => CaptureSnapshot(kind, region, cancellationToken);

    private Task<ImageSnapshot> CaptureSnapshot(SnapshotKind kind, PixelRegion? region, CancellationToken cancellationToken)
    {
        _runtime.Lifetime.ThrowIfStopping();
        return _runtime.Snapshots.CaptureAsync(
            () => _runtime.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, region, cancellationToken);
    }

}
