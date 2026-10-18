using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc/>
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken cancellationToken = default)
        => _host.Control.CaptureSnapshotAsync(kind, cancellationToken);

    /// <inheritdoc/>
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken cancellationToken = default)
        => _host.Control.CaptureSnapshotAsync(kind, region, cancellationToken);

}
