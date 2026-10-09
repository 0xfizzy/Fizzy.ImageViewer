namespace Fizzy.ImageViewer.Frames;

public sealed class RegionPixels(FrameInfo sourceFrame, PixelRegion region, ImageFrame pixels) : IDisposable
{
    private ImageFrame? _pixels = pixels;
    public FrameInfo SourceFrame { get; } = sourceFrame;
    public PixelRegion Region { get; } = region;
    public FrameLease AcquirePixels() => (Volatile.Read(ref _pixels) ?? throw new ObjectDisposedException(nameof(RegionPixels))).Acquire();
    internal ImageFrame TransferPixels() => Interlocked.Exchange(ref _pixels, null) ?? throw new ObjectDisposedException(nameof(RegionPixels));
    public void Dispose() => Interlocked.Exchange(ref _pixels, null)?.Dispose();
}
