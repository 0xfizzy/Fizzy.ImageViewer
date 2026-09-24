using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Rendering;

/// <summary>Owns one frame lease together with its atomically captured display settings.</summary>
internal sealed class CommittedViewLease(FrameLease frame, GrayDisplayRange? range, long version) : IDisposable
{
    internal FrameLease Frame { get; } = frame;
    internal GrayDisplayRange? Range { get; } = range;
    internal long Version { get; } = version;
    internal CommittedViewLease Acquire() => new(Frame.Acquire(), Range, Version);
    public void Dispose() => Frame.Dispose();
}
