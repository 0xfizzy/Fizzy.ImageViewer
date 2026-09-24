using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owned immutable query snapshot. Collections never borrow query buffers.</summary>
public sealed class MeasurementResult
{
    public Guid MeasurementId { get; }
    public long GeometryVersion { get; }
    public FrameInfo Frame { get; }
    /// <summary>Selects the valid payload: pixel samples, ordered line samples, or region statistics.</summary>
    public MeasurementQuery Query { get; }
    /// <summary>Source coordinates paired with Samples for Pixel and LineProfile; empty for RegionStatistics.</summary>
    public IReadOnlyList<PixelCoordinate> Coordinates { get; }
    /// <summary>One sample for Pixel, ordered samples for LineProfile; empty for RegionStatistics.</summary>
    public IReadOnlyList<PixelSample> Samples { get; }
    /// <summary>Clipped source region for RegionStatistics; null for sample queries.</summary>
    public PixelRegion? Region { get; }
    /// <summary>Channel statistics for RegionStatistics; empty for sample queries.</summary>
    public IReadOnlyList<ChannelStatistics> Channels { get; }
    // Arrays are exclusively owned snapshots (coordinates are immutable query geometry).
    internal MeasurementResult(Guid id, long version, FrameInfo frame, MeasurementQuery query,
        PixelCoordinate[] coordinates, PixelSample[] samples, PixelRegion? region, ChannelStatistics[] channels)
    {
        MeasurementId = id; GeometryVersion = version; Frame = frame; Query = query;
        Coordinates = Array.AsReadOnly(coordinates);
        Samples = Array.AsReadOnly(samples);
        Region = region; Channels = Array.AsReadOnly(channels);
    }
}
