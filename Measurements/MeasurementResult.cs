using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owned immutable query snapshot. Collections never borrow query buffers.</summary>
public sealed class MeasurementResult
{
    public Guid MeasurementId { get; }
    public long GeometryVersion { get; }
    public FrameInfo Frame { get; }
    public MeasurementQuery Query { get; }
    public IReadOnlyList<PixelCoordinate> Coordinates { get; }
    public IReadOnlyList<PixelSample> Samples { get; }
    public PixelRegion? Region { get; }
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
