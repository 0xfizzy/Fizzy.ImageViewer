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
    /// <summary>Layout of the retained pixels. Access after disposal throws ObjectDisposedException.</summary>
    public FrameDescriptor Descriptor => Storage.Descriptor;
    /// <summary>Borrows existing CPU memory without copying; returns false for GPU storage. Keep this lease alive while using the memory.</summary>
    public bool TryGetCpuPixels(out ReadOnlyMemory<byte> pixels)
    {
        var storage = Storage;
        pixels = storage.CpuPixels;
        return storage.Surface == 0;
    }
    internal ReadOnlyMemory<byte> CpuPixels => TryGetCpuPixels(out var pixels) ? pixels : throw new NotSupportedException("GPU pixels require an explicit asynchronous query.");
    /// <summary>Reads ordered original-pixel samples while retaining an independent source lease.
    /// CPU queries may execute synchronously on the calling thread; async does not imply worker dispatch.
    /// If querying and source cleanup both fail, an AggregateException preserves the query error first.</summary>
    public async ValueTask<PixelSamplesResult> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken cancellationToken = default)
    {
        var lease = Acquire();
        try
        {
            var copy = coordinates.ToArray();
            foreach (var point in copy)
                if ((uint)point.X >= (uint)lease.Descriptor.Width || (uint)point.Y >= (uint)lease.Descriptor.Height)
                    throw new ArgumentOutOfRangeException(nameof(coordinates));
            cancellationToken.ThrowIfCancellationRequested();
            var values = await lease.Storage.PixelSource.ReadPixelsAsync(copy, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (values.Length != copy.Length || values.Any(p => p.Format != lease.Descriptor.Format))
                throw new InvalidOperationException("Pixel source returned invalid samples.");
            var result = new PixelSamplesResult(lease.Info, values);
            lease.Dispose();
            return result;
        }
        catch (Exception error) { DisposeAfterFailure(error, lease); throw; }
    }
    /// <summary>Computes original-pixel statistics while retaining an independent source lease.
    /// CPU queries may execute synchronously on the calling thread.
    /// If querying and source cleanup both fail, an AggregateException preserves the query error first.</summary>
    public async ValueTask<RegionStatisticsResult> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken cancellationToken = default)
    {
        var lease = Acquire();
        try
        {
            region.Validate(lease.Descriptor);
            cancellationToken.ThrowIfCancellationRequested();
            var statistics = await lease.Storage.PixelSource.ComputeRegionStatisticsAsync(region, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (statistics.Format != lease.Descriptor.Format)
                throw new InvalidOperationException("Pixel source returned invalid statistics.");
            var result = new RegionStatisticsResult(lease.Info, region, statistics);
            lease.Dispose();
            return result;
        }
        catch (Exception error) { DisposeAfterFailure(error, lease); throw; }
    }
    /// <summary>Returns independently owned CPU pixels while retaining a source lease through the read.
    /// CPU queries may execute synchronously. If reading and cleanup fail, both errors are preserved,
    /// with the operation error first. A result is transferred only after source cleanup succeeds.</summary>
    public async ValueTask<RegionPixels> ReadRegionAsync(PixelRegion region, CancellationToken cancellationToken = default)
    {
        var lease = Acquire();
        ImageFrame? pixels = null;
        try
        {
            region.Validate(lease.Descriptor);
            cancellationToken.ThrowIfCancellationRequested();
            pixels = await lease.Storage.PixelSource.ReadRegionAsync(region, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using (var check = pixels.Acquire())
                if (check.Descriptor.Width != region.Width || check.Descriptor.Height != region.Height ||
                    check.Descriptor.Format != lease.Descriptor.Format || !check.TryGetCpuPixels(out _))
                    throw new InvalidOperationException("Pixel source returned an invalid CPU region.");
            lease.Dispose();
            return new(lease.Info, region, pixels);
        }
        catch (Exception error) { DisposeAfterFailure(error, pixels, lease); throw; }
    }

    // Failure-only path: each token is idempotent, including after a throwing final release.
    private static void DisposeAfterFailure(Exception error, params IDisposable?[] resources)
    {
        List<Exception>? errors = null;
        foreach (var resource in resources)
            try { resource?.Dispose(); }
            catch (Exception cleanup) { (errors ??= [error]).Add(cleanup); }
        if (errors != null) throw new AggregateException(errors);
    }
    internal nint D3D9Surface => Storage.Surface;
    /// <summary>Describes this lease's pixels and submission identity. A standalone lease has
    /// FrameId zero. Submission assigns identity only to the submitted lease; leases acquired
    /// before submission keep zero. Acquiring from a lease copies that lease's identity.</summary>
    public FrameInfo Info { get; internal set; }
    /// <summary>Acquires an independent reference with the same frame identity. The caller must dispose it; it can outlive this lease and the viewer.</summary>
    public FrameLease Acquire()
    {
        var lease = Storage.Acquire();
        lease.Info = Info;
        return lease;
    }
    /// <summary>Releases this token once. Final storage release runs synchronously on this thread,
    /// which may be the Viewer STA; producers must satisfy any thread affinity and avoid blocking.
    /// A throwing release is not retried by subsequent disposal.</summary>
    public void Dispose() => Interlocked.Exchange(ref _storage, null)?.Release();
}
