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
    /// <summary>Saves using an independent pixel lease. Operation and cleanup failures are
    /// preserved together, with the operation failure first. A cleanup failure can be reported
    /// after the destination was successfully replaced.</summary>
    public Task SaveAsync(string path, SnapshotEncoding encoding, CancellationToken ct = default)
    {
        var lease = AcquirePixels();
        return Task.Run(() =>
        {
            try
            {
                SnapshotEncoder.Save(lease, Kind, path, encoding, ct);
                lease.Dispose();
            }
            catch (Exception error)
            {
                try { lease.Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        });
    }
    public void Dispose() => _frame.Dispose();
}
