using System.Buffers;

namespace ApplicationEndpoint;

// The owner must support nonblocking disposal on any final-releasing thread.
internal sealed class GrayFrame(int width, int height, int stride, IMemoryOwner<byte> owner) : IDisposable
{
    private IMemoryOwner<byte>? _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = stride;
    public ReadOnlyMemory<byte> Pixels => (_owner ?? throw new ObjectDisposedException(nameof(GrayFrame))).Memory;
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Dispose();
}
