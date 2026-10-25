using System.Buffers;

namespace ApplicationEndpoint;

// Ordinary per-frame allocation; no pool or additional cache.
internal sealed class ArrayMemoryOwner(int size) : IMemoryOwner<byte>
{
    private byte[]? _pixels = new byte[size];
    public Memory<byte> Memory => _pixels ?? throw new ObjectDisposedException(nameof(ArrayMemoryOwner));
    public void Dispose() => Interlocked.Exchange(ref _pixels, null);
}
