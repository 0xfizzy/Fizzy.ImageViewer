using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    internal MenuSnapshotSession.Target? AcquireMenuSnapshot() => _host.MenuSession.AcquireTarget();
    internal MenuSnapshotSession.Target? AcquireMenuRegionSnapshot() => _host.MenuSession.AcquireTarget(region: true);
    internal void Freeze() => _host.MenuSession.Open();
    internal void Unfreeze() => _host.MenuSession.Close();
    internal void FreezeMenuRegion() => _host.MenuController.CaptureTarget();

    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken ct = default)
        => _host.Snapshots.CaptureAsync(_host.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, ct: ct);

    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken ct = default)
        => _host.Snapshots.CaptureAsync(_host.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, region, ct);

    // The target is already acquired, so capture remains valid after Viewer shutdown.
    internal Task<ImageSnapshot> CaptureSnapshotAsync(MenuSnapshotSession.Target target, SnapshotKind kind, CancellationToken ct)
        => _host.Snapshots.CaptureAsync(target.View, kind, target.Region, ct);
}
