using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

/// <summary>Independent immutable pixels. Owns its storage until disposed.</summary>
public sealed class ImageSnapshot : IDisposable
{
    private readonly ImageFrame _frame;
    internal ImageSnapshot(ImageFrame frame, FrameInfo info, SnapshotKind kind, long displayVersion, PixelRegion region)
    { _frame = frame; SourceFrame = info; Kind = kind; DisplayVersion = displayVersion; Region = region; }
    /// <summary>Captured region in original source-frame coordinates.</summary>
    public PixelRegion Region { get; }
    /// <summary>Identity and layout of the committed source frame.</summary>
    public FrameInfo SourceFrame { get; }
    /// <summary>Whether pixels preserve original values or apply the committed display mapping.</summary>
    public SnapshotKind Kind { get; }
    /// <summary>Display-settings version captured with the committed source frame.</summary>
    public long DisplayVersion { get; }
    /// <summary>Acquires an owned lease to snapshot pixels with region-local coordinates and standalone frame identity. It may outlive the snapshot and viewer.</summary>
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
    /// <summary>Releases the snapshot reference once; independent pixel leases remain valid. Final-release failures propagate and are not retried.</summary>
    public void Dispose() => _frame.Dispose();
}
