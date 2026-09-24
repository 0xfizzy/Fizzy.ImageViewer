namespace Fizzy.ImageViewer.Frames;

internal sealed class FrameStorage(FrameDescriptor descriptor, ReadOnlyMemory<byte> data,
    IFramePixelSource pixelSource, Action release, nint surface = 0)
{
    private int _references = 1;
    public FrameDescriptor Descriptor { get; } = descriptor;
    public ReadOnlyMemory<byte> CpuPixels { get; } = data;
    public IFramePixelSource PixelSource { get; } = pixelSource;
    public nint Surface { get; } = surface;
    public FrameLease Acquire()
    {
        int count;
        do
        {
            count = Volatile.Read(ref _references);
            if (count == 0) throw new ObjectDisposedException(nameof(ImageFrame));
        } while (Interlocked.CompareExchange(ref _references, checked(count + 1), count) != count);
        return new(this);
    }
    public void Release() { if (Interlocked.Decrement(ref _references) == 0) release(); }
}
