using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    internal MenuSnapshotSession.Target? AcquireMenuSnapshot() => _runtime.MenuSession.AcquireTarget();
    internal MenuSnapshotSession.Target? AcquireMenuRegionSnapshot() => _runtime.MenuSession.AcquireTarget(region: true);
    internal void Freeze() => _runtime.MenuSession.Open();
    internal void Unfreeze() => _runtime.MenuSession.Close();
    internal void FreezeMenuRegion() => _runtime.FreezeMenuRegion();

    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken ct = default)
        => _runtime.Snapshots.CaptureAsync(_runtime.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, ct: ct);

    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken ct = default)
        => _runtime.Snapshots.CaptureAsync(_runtime.Pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, region, ct);

    // The target is already acquired, so capture remains valid after Viewer shutdown.
    internal Task<ImageSnapshot> CaptureSnapshotAsync(MenuSnapshotSession.Target target, SnapshotKind kind, CancellationToken ct)
        => _runtime.Snapshots.CaptureAsync(target.View, kind, target.Region, ct);
}
