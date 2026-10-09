namespace Fizzy.ImageViewer.Frames;

/// <summary>Ordered original-pixel samples returned by FrameLease.ReadPixelsAsync, with frame
/// provenance. Construction copies the supplied samples; retain the requested coordinates
/// separately when their association is needed.</summary>
public sealed class PixelSamplesResult
{
    private readonly PixelSample[] _samples;
    public FrameInfo Frame { get; }
    public IReadOnlyList<PixelSample> Samples { get; }
    internal ReadOnlyMemory<PixelSample> SampleMemory => _samples;
    public PixelSamplesResult(FrameInfo frame, IEnumerable<PixelSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        Frame = frame;
        _samples = samples.ToArray();
        Samples = Array.AsReadOnly(_samples);
    }
}
