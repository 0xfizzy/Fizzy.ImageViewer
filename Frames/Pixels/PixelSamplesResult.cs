namespace Fizzy.ImageViewer.Frames;

/// <summary>Ordered original-pixel samples returned by FrameLease.ReadPixelsAsync, with source frame
/// provenance. Construction copies the supplied samples; retain the requested coordinates
/// separately when their association is needed.</summary>
public sealed class PixelSamplesResult
{
    private readonly PixelSample[] _samples;
    /// <summary>Identity and layout of the queried source frame.</summary>
    public FrameInfo SourceFrame { get; }
    /// <summary>Immutable samples in request order, safe to retain after the source lease is released.</summary>
    public IReadOnlyList<PixelSample> Samples { get; }
    internal ReadOnlyMemory<PixelSample> SampleMemory => _samples;
    /// <summary>Creates a result by copying the supplied samples and retaining their source frame provenance.</summary>
    public PixelSamplesResult(FrameInfo sourceFrame, IEnumerable<PixelSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        SourceFrame = sourceFrame;
        _samples = samples.ToArray();
        Samples = Array.AsReadOnly(_samples);
    }
}
