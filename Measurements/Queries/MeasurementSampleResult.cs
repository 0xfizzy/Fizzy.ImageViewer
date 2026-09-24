using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owned coordinate/sample pairs for a pixel or ordered line-profile query.</summary>
public sealed class MeasurementSampleResult : MeasurementQueryResult
{
    public IReadOnlyList<PixelCoordinate> Coordinates { get; }
    public IReadOnlyList<PixelSample> Samples { get; }
    internal MeasurementSampleResult(Guid id, long version, FrameInfo frame, MeasurementQueryKind query,
        PixelCoordinate[] coordinates, PixelSample[] samples) : base(id, version, frame, query)
    {
        if (query is not (MeasurementQueryKind.Pixel or MeasurementQueryKind.LineProfile))
            throw new ArgumentOutOfRangeException(nameof(query));
        if (coordinates.Length != samples.Length || (query == MeasurementQueryKind.Pixel && samples.Length != 1))
            throw new ArgumentException("Coordinates and samples must form valid query pairs.");
        Coordinates = Array.AsReadOnly((PixelCoordinate[])coordinates.Clone());
        Samples = Array.AsReadOnly((PixelSample[])samples.Clone());
    }
}
