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
        try
        {
            data[..descriptor.RequiredBytes].CopyTo(owner.Memory.Span);
            return CreateCpuFrame(descriptor, owner.Memory[..descriptor.RequiredBytes], owner.Dispose);
        }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>Transfers ownership, including on validation failure. Release is called exactly once.</summary>
    public static ImageFrame TakeOwnership(FrameDescriptor descriptor, ReadOnlyMemory<byte> data, Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        try
        {
            descriptor.Validate(data.Length);
            return CreateCpuFrame(descriptor, data[..descriptor.RequiredBytes], release);
        }
        catch { release(); throw; }
    }

    private static ImageFrame CreateCpuFrame(FrameDescriptor descriptor, ReadOnlyMemory<byte> pixels, Action release)
        => new(new FrameStorage(descriptor, pixels, new CpuFramePixelSource(descriptor, pixels), release));

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
            return new(new FrameStorage(descriptor, default, pixelSource, release, surface));
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
