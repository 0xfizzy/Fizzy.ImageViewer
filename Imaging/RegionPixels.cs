using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

public sealed class RegionPixels(FrameInfo frame, PixelRegion region, ImageFrame pixels) : IDisposable
{
    public FrameInfo Frame { get; } = frame;
    public PixelRegion Region { get; } = region;
    public FrameLease AcquirePixels()
    {
        var lease = pixels.Acquire();
        lease.Info = Frame;
        return lease;
    }
    public void Dispose() => pixels.Dispose();
}
