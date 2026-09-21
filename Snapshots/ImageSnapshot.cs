using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

public enum SnapshotKind { Raw, Display }
public enum SnapshotEncoding { Tiff, Png, Jpeg, Bmp }

/// <summary>Independent immutable pixels. Owns its storage until disposed.</summary>
public sealed class ImageSnapshot : IDisposable
{
    private readonly ImageFrame _frame;
    internal ImageSnapshot(ImageFrame frame, FrameInfo info, SnapshotKind kind, long displayVersion, PixelRegion region)
    { _frame = frame; Frame = info; Kind = kind; DisplayVersion = displayVersion; Region = region; }
    public PixelRegion Region { get; }
    public FrameInfo Frame { get; }
    public SnapshotKind Kind { get; }
    public long DisplayVersion { get; }
    public FrameLease AcquirePixels()
    {
        var lease = _frame.Acquire();
        lease.Info = Frame;
        return lease;
    }
    public Task SaveAsync(string path, SnapshotEncoding encoding, CancellationToken ct = default)
    {
        var lease = AcquirePixels();
        return Task.Run(() =>
        {
            using (lease) SnapshotEncoder.Save(lease, Kind, path, encoding, ct);
        });
    }
    public void Dispose() => _frame.Dispose();
}
