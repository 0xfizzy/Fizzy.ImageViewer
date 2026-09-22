using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private SnapshotRequest? _menuSnapshot;
    private int _saving;
    private readonly SemaphoreSlim _exportGate = new(1,1);
    private PixelRegion? _menuRegion;
    internal bool HasMenuRegion => _menuRegion is { IsEmpty: false };
    internal bool TryBeginSave() => Interlocked.CompareExchange(ref _saving, 1, 0) == 0;
    internal void EndSave() => Interlocked.Exchange(ref _saving, 0);
    internal sealed record SnapshotRequest(FrameLease Frame, GrayDisplayRange? Range, long Version, PixelRegion? Region = null, SemaphoreSlim? Gate = null) : IDisposable
    {
        public SnapshotRequest Acquire() => new(Frame.Acquire(), Range, Version, Region, Gate);
        public void Dispose() => Frame.Dispose();
    }
    internal SnapshotRequest? AcquireMenuSnapshot()
    {
        lock (_frameGate) return _menuSnapshot?.Acquire() ?? (_currentFrame == null ? null : new(_currentFrame.Acquire(), _committedGrayRange, _committedDisplayVersion, Gate: _exportGate));
    }
    internal SnapshotRequest? AcquireMenuRegionSnapshot()
    {
        var snapshot=AcquireMenuSnapshot();
        if(snapshot==null)return null;
        if(_menuRegion is not { IsEmpty:false } region) {snapshot.Dispose();return null;}
        return snapshot with { Region=region };
    }
    internal void FreezeMenuRegion()
    {
        Freeze(); _menuRegion=null;
        if (_interaction?.SelectedMeasurement is { IsComplete: true, Geometry.Kind: Enums.ShapeType.Rectangle } item)
        {
            using var snapshot = AcquireMenuSnapshot();
            if (snapshot != null) _menuRegion = item.Geometry.ToRegion(snapshot.Frame.Descriptor);
        }
    }
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, CancellationToken ct = default)
    {
        SnapshotRequest request;
        lock (_frameGate) { _lifetime.ThrowIfStopping(); request = _currentFrame == null ? throw new InvalidOperationException("No current frame.") :
            new(_currentFrame.Acquire(), _committedGrayRange, _committedDisplayVersion, Gate: _exportGate); }
        return CaptureSnapshotAsync(request, kind, ct);
    }
    public Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotKind kind, PixelRegion region, CancellationToken ct = default)
    {
        SnapshotRequest request;
        lock (_frameGate) { _lifetime.ThrowIfStopping(); request = _currentFrame == null ? throw new InvalidOperationException("No current frame.") :
            new(_currentFrame.Acquire(), _committedGrayRange, _committedDisplayVersion, region, _exportGate); }
        return CaptureSnapshotAsync(request,kind,ct);
    }
    internal static Task<ImageSnapshot> CaptureSnapshotAsync(SnapshotRequest request, SnapshotKind kind, CancellationToken ct)
        => Task.Run(async () =>
        {
            using (request)
            {
                if (kind is not SnapshotKind.Raw and not SnapshotKind.Display) throw new ArgumentOutOfRangeException(nameof(kind));
                if(request.Gate!=null) await request.Gate.WaitAsync(ct).ConfigureAwait(false);
                try {
                ct.ThrowIfCancellationRequested();
                var region=request.Region ?? PixelRegion.Full(request.Frame.Descriptor);
                using var read = await request.Frame.ReadRegionAsync(region, ct).ConfigureAwait(false);
                using var cpu = read.AcquirePixels();
                if (kind == SnapshotKind.Raw)
                    return new ImageSnapshot(ImageFrame.Copy(cpu.Descriptor, cpu.CpuPixels.Span), request.Frame.Info, kind, request.Version, region);
                if (kind != SnapshotKind.Display) throw new ArgumentOutOfRangeException(nameof(kind));
                using var pixels = DisplayConverter.Convert(cpu, request.Range, ct, normalize: true);
                return new ImageSnapshot(ImageFrame.Copy(new(pixels.Width, pixels.Height, pixels.Stride, FramePixelFormat.Pbgra32), pixels.Bytes.AsSpan(0, pixels.Length)), request.Frame.Info, kind, request.Version, region);
                } finally { request.Gate?.Release(); }
            }
        });
}
