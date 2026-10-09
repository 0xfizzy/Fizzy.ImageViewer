using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owned statistics for a nonempty clipped source region.</summary>
public sealed class MeasurementRegionStatisticsResult : MeasurementQueryResult
{
    /// <summary>Nonempty queried region after clipping to the source frame.</summary>
    public PixelRegion Region { get; }
    /// <summary>Immutable statistics in Gray, R/G/B or R/G/B/A order; padding is excluded and premultiplied values remain premultiplied.</summary>
    public IReadOnlyList<ChannelStatistics> Channels { get; }
    internal MeasurementRegionStatisticsResult(Guid id, long version, FrameInfo frame,
        PixelRegion region, ChannelStatistics[] channels) : base(id, version, frame, MeasurementQueryKind.RegionStatistics)
    {
        if (region.IsEmpty) throw new ArgumentException("Region must not be empty.", nameof(region));
        Region = region;
        Channels = Array.AsReadOnly((ChannelStatistics[])channels.Clone());
    }
}
