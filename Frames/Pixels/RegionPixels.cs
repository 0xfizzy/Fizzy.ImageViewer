namespace Fizzy.ImageViewer.Frames;

/// <summary>Owns independently read CPU pixels and their source provenance.
/// Created by <see cref="FrameLease.ReadRegionAsync"/> after validating the provider result.</summary>
public sealed class RegionPixels : IDisposable
{
    private ImageFrame? _pixels;
    // The caller validates pixels and transfers ownership only after source cleanup succeeds.
    internal RegionPixels(FrameInfo sourceFrame, PixelRegion region, ImageFrame pixels)
    { SourceFrame = sourceFrame; Region = region; _pixels = pixels; }
    public FrameInfo SourceFrame { get; }
    public PixelRegion Region { get; }
    public FrameLease AcquirePixels() => (Volatile.Read(ref _pixels) ?? throw new ObjectDisposedException(nameof(RegionPixels))).Acquire();
    internal ImageFrame TransferPixels() => Interlocked.Exchange(ref _pixels, null) ?? throw new ObjectDisposedException(nameof(RegionPixels));
    public void Dispose() => Interlocked.Exchange(ref _pixels, null)?.Dispose();
}
