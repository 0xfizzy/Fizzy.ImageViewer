using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken ct = default)
        => CaptureSnapshot(kind, null, ct);

    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken ct = default)
        => CaptureSnapshot(kind, region, ct);

    private Task<ImageSnapshot> CaptureSnapshot(SnapshotKind kind, PixelRegion? region, CancellationToken ct)
    {
        _host.Lifetime.ThrowIfStopping();
        return _host.Snapshots.CaptureAsync(
            () => _host.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, region, ct);
    }

}
