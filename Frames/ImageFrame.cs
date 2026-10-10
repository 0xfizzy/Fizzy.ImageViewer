using System.Buffers;

namespace Fizzy.ImageViewer.Frames;

/// <summary>Owned, immutable pixel storage. Do not mutate or release transferred storage.</summary>
public sealed class ImageFrame : IDisposable
{
    private FrameStorage? _storage;
    internal ImageFrame(FrameStorage storage) => _storage = storage;
    /// <summary>Layout of the owned pixels. Access after ownership transfer or disposal throws ObjectDisposedException.</summary>
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
        catch (Exception error)
        {
            ReleaseAfterFailure(owner.Dispose, error);
            throw;
        }
    }

    /// <summary>Transfers ownership, including on validation failure. Release is called exactly once.
    /// Release runs synchronously on the final releasing thread, potentially the Viewer STA;
    /// the producer must satisfy thread affinity and avoid blocking.
    /// If construction and release both fail, throws an AggregateException containing both errors.</summary>
    public static ImageFrame TakeOwnership(FrameDescriptor descriptor, ReadOnlyMemory<byte> data, Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        try
        {
            descriptor.Validate(data.Length);
            return CreateCpuFrame(descriptor, data[..descriptor.RequiredBytes], release);
        }
        catch (Exception error)
        {
            ReleaseAfterFailure(release, error);
            throw;
        }
    }

    private static ImageFrame CreateCpuFrame(FrameDescriptor descriptor, ReadOnlyMemory<byte> pixels, Action release)
        => new(new FrameStorage(descriptor, pixels, new CpuFramePixelSource(descriptor, pixels), release));

    /// <summary>Acquires an independent reference to the pixels. The caller must dispose it; it can outlive this frame and the viewer.</summary>
    public FrameLease Acquire() => Storage.Acquire();
    /// <summary>Transfers an immutable, GPU-ready BGRA IDirect3DSurface9 and its owner.
    /// The query provider reads only explicitly requested pixels and must return
    /// independent immutable CPU storage matching descriptor. Release runs after the last lease.
    /// Release runs synchronously on the final releasing thread, potentially the Viewer STA;
    /// the producer must satisfy thread affinity and avoid blocking.
    /// Ownership transfers even on construction failure. If construction and release both fail,
    /// throws an AggregateException containing both errors.</summary>
    /// <remarks>The pointer must identify a valid, non-multisampled D3D9 render-target surface
    /// in D3DPOOL_DEFAULT, with D3DFMT_A8R8G8B8 pixels and dimensions matching descriptor.
    /// Alpha must be premultiplied for WPF. The descriptor describes original query pixels,
    /// not the BGRA display layout. The producer must finish GPU writes and relinquish any
    /// compute mapping before transfer, and keep the surface and its device valid and immutable
    /// until release. The library does not inspect native resource metadata, wait producer fences,
    /// rebuild devices or provide software fallback. Surface pixels must match the standard
    /// display conversion of the original pixels with no custom display range or color mapping.</remarks>
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
        catch (Exception error)
        {
            ReleaseAfterFailure(release, error);
            throw;
        }
    }
    private static void ReleaseAfterFailure(Action release, Exception error)
    {
        try { release(); }
        catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
    }
    internal FrameLease Transfer()
    {
        var storage = Interlocked.Exchange(ref _storage, null) ?? throw new ObjectDisposedException(nameof(ImageFrame));
        return new FrameLease(storage);
    }
    /// <summary>Releases this frame reference once. Final release runs synchronously and propagates cleanup failure; subsequent disposal does not retry it.</summary>
    public void Dispose() => Interlocked.Exchange(ref _storage, null)?.Release();
}
