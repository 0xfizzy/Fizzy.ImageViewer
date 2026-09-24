using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

/// <summary>Independent immutable pixels. Owns its storage until disposed.</summary>
public sealed class ImageSnapshot : IDisposable
{
    private readonly ImageFrame _frame;
    internal ImageSnapshot(ImageFrame frame, FrameInfo info, SnapshotKind kind, long displayVersion, PixelRegion region)
    { _frame = frame; SourceFrame = info; Kind = kind; DisplayVersion = displayVersion; Region = region; }
    public PixelRegion Region { get; }
    public FrameInfo SourceFrame { get; }
    public SnapshotKind Kind { get; }
    public long DisplayVersion { get; }
    public FrameLease AcquirePixels() => _frame.Acquire();
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
