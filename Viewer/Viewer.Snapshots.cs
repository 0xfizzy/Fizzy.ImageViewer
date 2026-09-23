using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private MenuSnapshotSession _menuSession = null!;
    private readonly SnapshotCapture _snapshotCapture = new();

    internal MenuSnapshotSession.Target? AcquireMenuSnapshot() => _menuSession.AcquireTarget();
    internal MenuSnapshotSession.Target? AcquireMenuRegionSnapshot() => _menuSession.AcquireTarget(region: true);
    internal void Freeze() => _menuSession.Open();
    internal void Unfreeze() => _menuSession.Close();
    internal void FreezeMenuRegion() => _menuSession.Open(descriptor =>
        _interaction?.SelectedMeasurement is { IsComplete: true, Geometry.Kind: Drawing.ShapeType.Rectangle } item
            ? item.Geometry.ToRegion(descriptor) : null);

    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken ct = default)
        => _snapshotCapture.CaptureAsync(_pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, ct: ct);

    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken ct = default)
        => _snapshotCapture.CaptureAsync(_pipeline.AcquireCommittedView() ?? throw new InvalidOperationException("No current frame."), kind, region, ct);

    // The target is already acquired, so capture remains valid after Viewer shutdown.
    internal Task<ImageSnapshot> CaptureSnapshotAsync(MenuSnapshotSession.Target target, SnapshotKind kind, CancellationToken ct)
        => _snapshotCapture.CaptureAsync(target.View, kind, target.Region, ct);
}
