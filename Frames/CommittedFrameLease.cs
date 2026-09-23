using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Frames;

/// <summary>Owns one frame lease together with its atomically captured display settings.</summary>
internal sealed class CommittedFrameLease(FrameLease frame, GrayDisplayRange? range, long version) : IDisposable
{
    internal FrameLease Frame { get; } = frame;
    internal GrayDisplayRange? Range { get; } = range;
    internal long Version { get; } = version;
    internal CommittedFrameLease Acquire() => new(Frame.Acquire(), Range, Version);
    public void Dispose() => Frame.Dispose();
}
