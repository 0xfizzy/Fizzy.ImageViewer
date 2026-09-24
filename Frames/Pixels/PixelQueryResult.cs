namespace Fizzy.ImageViewer.Frames;

/// <summary>Immutable pixel snapshot. Construction copies the supplied samples.</summary>
public sealed class PixelQueryResult
{
    private readonly PixelSample[] _samples;
    public FrameInfo Frame { get; }
    public IReadOnlyList<PixelSample> Samples { get; }
    internal ReadOnlyMemory<PixelSample> SampleMemory => _samples;
    public PixelQueryResult(FrameInfo frame, IEnumerable<PixelSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        Frame = frame;
        _samples = samples.ToArray();
        Samples = Array.AsReadOnly(_samples);
    }
}
