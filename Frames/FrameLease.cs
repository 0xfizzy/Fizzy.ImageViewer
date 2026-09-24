using Fizzy.ImageViewer.Imaging;
namespace Fizzy.ImageViewer.Frames;

/// <summary>A single ownership token. Do not dispose concurrently with reading its memory.</summary>
public sealed class FrameLease : IDisposable
{
    private FrameStorage? _storage;
    internal FrameLease(FrameStorage storage)
    {
        _storage = storage;
        Info = new(0, storage.Descriptor, null);
    }
    private FrameStorage Storage => Volatile.Read(ref _storage) ?? throw new ObjectDisposedException(nameof(FrameLease));
    public FrameDescriptor Descriptor => Storage.Descriptor;
    public bool TryGetCpuPixels(out ReadOnlyMemory<byte> pixels)
    {
        var storage=Storage; pixels=storage.CpuPixels; return storage.Surface==0;
    }
    internal ReadOnlyMemory<byte> CpuPixels => TryGetCpuPixels(out var pixels) ? pixels : throw new NotSupportedException("GPU pixels require an explicit asynchronous query.");
    public async ValueTask<PixelQueryResult> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct = default)
    {
        using var lease=Acquire();
        var copy=coordinates.ToArray();
        foreach(var p in copy) if((uint)p.X >= (uint)lease.Descriptor.Width || (uint)p.Y >= (uint)lease.Descriptor.Height) throw new ArgumentOutOfRangeException(nameof(coordinates));
        ct.ThrowIfCancellationRequested();
        var values=await lease.Storage.PixelSource.ReadPixelsAsync(copy,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (values.Length != copy.Length || values.Any(p => p.Format != lease.Descriptor.Format))
            throw new InvalidOperationException("Pixel source returned invalid samples.");
        return new(lease.Info,values);
    }
    public async ValueTask<RegionStatisticsResult> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct = default)
    {
        using var lease=Acquire(); region.Validate(lease.Descriptor); ct.ThrowIfCancellationRequested();
        var result=await lease.Storage.PixelSource.ComputeRegionStatisticsAsync(region,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var format = lease.Descriptor.Format;
        int channels = format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float ? 1 :
            format is FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32 ? 4 : 3;
        if (result.Format != format || result.Channels.Count != channels)
            throw new InvalidOperationException("Pixel source returned invalid statistics.");
        return new(lease.Info,region,result);
    }
    public async ValueTask<RegionPixels> ReadRegionAsync(PixelRegion region, CancellationToken ct = default)
    {
        using var lease=Acquire(); region.Validate(lease.Descriptor); ct.ThrowIfCancellationRequested();
        var pixels=await lease.Storage.PixelSource.ReadRegionAsync(region,ct).ConfigureAwait(false);
        if(ct.IsCancellationRequested) { pixels.Dispose(); ct.ThrowIfCancellationRequested(); }
        using (var check = pixels.Acquire())
        {
            if (check.Descriptor.Width != region.Width || check.Descriptor.Height != region.Height ||
                check.Descriptor.Format != lease.Descriptor.Format || !check.TryGetCpuPixels(out _))
            {
                pixels.Dispose();
                throw new InvalidOperationException("Pixel source returned an invalid CPU region.");
            }
        }
        return new(lease.Info,region,pixels);
    }
    internal nint D3D9Surface => Storage.Surface;
    /// <summary>Describes these pixels. FrameId is zero until submitted to a viewer.</summary>
    public FrameInfo Info { get; internal set; }
    public FrameLease Acquire()
    {
        var lease = Storage.Acquire();
        lease.Info = Info;
        return lease;
    }
    public void Dispose() => Interlocked.Exchange(ref _storage, null)?.Release();
}
