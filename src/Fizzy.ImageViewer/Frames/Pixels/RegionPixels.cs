namespace Fizzy.ImageViewer.Frames;

/// <summary>Owns independently read CPU pixels and their source provenance.
/// Created by <see cref="FrameLease.ReadRegionAsync"/> after validating the provider result.</summary>
public sealed class RegionPixels : IDisposable
{
    private ImageFrame? _pixels;
    // The caller validates pixels and transfers ownership only after source cleanup succeeds.
    internal RegionPixels(FrameInfo sourceFrame, PixelRegion region, ImageFrame pixels)
    { SourceFrame = sourceFrame; Region = region; _pixels = pixels; }
    /// <summary>Identity and layout of the original source frame.</summary>
    public FrameInfo SourceFrame { get; }
    /// <summary>Read region in original source-frame coordinates.</summary>
    public PixelRegion Region { get; }
    /// <summary>Acquires an independently disposable lease with region-local coordinates and standalone frame identity; it may outlive this result.</summary>
    public FrameLease AcquirePixels() => (Volatile.Read(ref _pixels) ?? throw new ObjectDisposedException(nameof(RegionPixels))).Acquire();
    internal ImageFrame TransferPixels() => Interlocked.Exchange(ref _pixels, null) ?? throw new ObjectDisposedException(nameof(RegionPixels));
    /// <summary>Releases this result's pixel reference once. Independent leases remain valid; final-release failures propagate and are not retried.</summary>
    public void Dispose() => Interlocked.Exchange(ref _pixels, null)?.Dispose();
}
