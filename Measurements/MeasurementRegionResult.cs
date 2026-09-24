using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owned statistics for a nonempty clipped source region.</summary>
public sealed class MeasurementRegionResult : MeasurementQueryResult
{
    public PixelRegion Region { get; }
    public IReadOnlyList<ChannelStatistics> Channels { get; }
    internal MeasurementRegionResult(Guid id, long version, FrameInfo frame,
        PixelRegion region, ChannelStatistics[] channels) : base(id, version, frame, MeasurementQueryKind.RegionStatistics)
    {
        if (region.IsEmpty) throw new ArgumentException("Region must not be empty.", nameof(region));
        Region = region;
        Channels = Array.AsReadOnly((ChannelStatistics[])channels.Clone());
    }
}
