namespace Fizzy.ImageViewer.Frames;

/// <summary>Immutable statistics snapshot. Construction copies the supplied channels.</summary>
public sealed class RegionStatistics
{
    /// <summary>Original storage format that determines semantic channel count and interpretation.</summary>
    public FramePixelFormat Format { get; }
    /// <summary>Semantic order: Gray, R/G/B, or R/G/B/A. Bgr32 padding is excluded;
    /// premultiplied color values remain premultiplied.</summary>
    public IReadOnlyList<ChannelStatistics> Channels { get; }
    /// <summary>Copies channel statistics in semantic order; rejects unknown formats and a channel count inconsistent with the format.</summary>
    public RegionStatistics(FramePixelFormat format, IEnumerable<ChannelStatistics> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var info = format.GetInfo();
        var snapshot = channels.ToArray();
        if (snapshot.Length != info.SemanticChannelCount)
            throw new ArgumentException("Channel count must match the pixel format.", nameof(channels));
        Format = format;
        Channels = Array.AsReadOnly(snapshot);
    }
}
