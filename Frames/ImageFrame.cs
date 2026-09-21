using Fizzy.ImageViewer.Imaging;
using System.Buffers;

namespace Fizzy.ImageViewer.Frames;

/// <summary>Owned, immutable pixel storage. Do not mutate or release transferred storage.</summary>
public sealed class ImageFrame : IDisposable
{
    private FrameStorage? _storage;
    internal ImageFrame(FrameStorage storage) => _storage = storage;
    public FrameDescriptor Descriptor => Storage.Descriptor;
    private FrameStorage Storage => Volatile.Read(ref _storage) ?? throw new ObjectDisposedException(nameof(ImageFrame));

    /// <summary>Copies before returning. The caller retains ownership of the input.</summary>
    public static ImageFrame Copy(FrameDescriptor descriptor, ReadOnlySpan<byte> data)
    {
        descriptor.Validate(data.Length);
        var owner = MemoryPool<byte>.Shared.Rent(descriptor.RequiredBytes);
        data[..descriptor.RequiredBytes].CopyTo(owner.Memory.Span);
        return new(new(descriptor, owner.Memory[..descriptor.RequiredBytes], owner.Dispose));
    }

    /// <summary>Transfers ownership, including on validation failure. Release is called exactly once.</summary>
    public static ImageFrame TakeOwnership(FrameDescriptor descriptor, ReadOnlyMemory<byte> data, Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        try { descriptor.Validate(data.Length); return new(new(descriptor, data[..descriptor.RequiredBytes], release)); }
        catch { release(); throw; }
    }

    public FrameLease Acquire() => Storage.Acquire();
    /// <summary>Transfers an immutable, GPU-ready BGRA IDirect3DSurface9 and its owner.
    /// The query provider reads only explicitly requested pixels and must return
    /// independent immutable CPU storage matching descriptor. Release runs after the last lease.</summary>
    public static ImageFrame TakeD3D9Surface(FrameDescriptor descriptor, nint surface,
        IFramePixelSource pixelSource, Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        try
        {
            descriptor.Validate(descriptor.RequiredBytes);
            if (surface == 0) throw new ArgumentException("A ready D3D9 surface is required.", nameof(surface));
            ArgumentNullException.ThrowIfNull(pixelSource);
            return new(new FrameStorage(descriptor, default, release, surface, pixelSource));
        }
        catch { release(); throw; }
    }
    internal FrameLease Transfer()
    {
        var storage = Interlocked.Exchange(ref _storage, null) ?? throw new ObjectDisposedException(nameof(ImageFrame));
        return new FrameLease(storage);
    }
    public void Dispose() => Interlocked.Exchange(ref _storage, null)?.Release();
}

internal sealed class FrameStorage(FrameDescriptor descriptor, ReadOnlyMemory<byte> data, Action release,
    nint surface = 0, IFramePixelSource? pixelSource = null)
{
    private int _references = 1;
    public FrameDescriptor Descriptor { get; } = descriptor;
    public ReadOnlyMemory<byte> CpuPixels { get; } = data;
    public IFramePixelSource PixelSource { get; } = pixelSource ?? new CpuFramePixelSource(descriptor, data);
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
