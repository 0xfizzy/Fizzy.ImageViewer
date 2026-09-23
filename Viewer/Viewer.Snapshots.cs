using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken ct = default)
        => _host.Snapshots.CaptureAsync(_host.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, ct: ct);

    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken ct = default)
        => _host.Snapshots.CaptureAsync(_host.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, region, ct);

}
