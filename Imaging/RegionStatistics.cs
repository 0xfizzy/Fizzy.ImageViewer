using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

/// <summary>Immutable statistics snapshot. Construction copies the supplied channels.</summary>
public sealed class RegionStatistics
{
    public FramePixelFormat Format { get; }
    public IReadOnlyList<ChannelStatistics> Channels { get; }
    public RegionStatistics(FramePixelFormat format, IEnumerable<ChannelStatistics> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        Format = format;
        Channels = Array.AsReadOnly(channels.ToArray());
    }
}
