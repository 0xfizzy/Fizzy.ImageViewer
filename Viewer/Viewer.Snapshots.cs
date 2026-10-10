using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc/>
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken cancellationToken = default)
        => CaptureSnapshot(kind, null, cancellationToken);

    /// <inheritdoc/>
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken cancellationToken = default)
        => CaptureSnapshot(kind, region, cancellationToken);

    private Task<ImageSnapshot> CaptureSnapshot(SnapshotKind kind, PixelRegion? region, CancellationToken cancellationToken)
    {
        _host.Lifetime.ThrowIfStopping();
        return _host.Snapshots.CaptureAsync(
            () => _host.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, region, cancellationToken);
    }

}
