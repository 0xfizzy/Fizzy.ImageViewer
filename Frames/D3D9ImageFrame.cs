namespace Fizzy.ImageViewer.Frames;

/// <summary>Creates owned frames from ready, immutable D3D9 display surfaces.</summary>
public static class D3D9ImageFrame
{
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
    public static ImageFrame TakeOwnership(FrameDescriptor descriptor, nint surface,
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
            try { release(); }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
    }
}
