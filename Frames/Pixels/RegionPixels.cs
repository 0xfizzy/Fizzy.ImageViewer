namespace Fizzy.ImageViewer.Frames;

public sealed class RegionPixels(FrameInfo sourceFrame, PixelRegion region, ImageFrame pixels) : IDisposable
{
    public FrameInfo SourceFrame { get; } = sourceFrame;
    public PixelRegion Region { get; } = region;
    public FrameLease AcquirePixels() => pixels.Acquire();
    public void Dispose() => pixels.Dispose();
}
