using Fizzy.ImageViewer.Frames;
using System.Collections.ObjectModel;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owned coordinate/sample pairs for a pixel or ordered line-profile query.</summary>
public sealed class MeasurementSampleResult : MeasurementQueryResult
{
    public IReadOnlyList<PixelCoordinate> Coordinates { get; }
    public IReadOnlyList<PixelSample> Samples { get; }
    internal MeasurementSampleResult(Guid id, long version, FrameInfo frame, MeasurementQueryKind query,
        PixelCoordinate[] coordinates, PixelSample[] samples)
        : this(id, version, frame, query, Array.AsReadOnly((PixelCoordinate[])coordinates.Clone()), samples.AsSpan())
    {
    }

    // The request owns an immutable coordinate snapshot shared by all of its results.
    // Samples borrow the scheduler's batch only during publication and must be copied.
    internal MeasurementSampleResult(Guid id, long version, FrameInfo frame, MeasurementQueryKind query,
        ReadOnlyCollection<PixelCoordinate> coordinates, ReadOnlySpan<PixelSample> samples) : base(id, version, frame, query)
    {
        if (query is not (MeasurementQueryKind.Pixel or MeasurementQueryKind.LineProfile))
            throw new ArgumentOutOfRangeException(nameof(query));
        if (coordinates.Count != samples.Length || (query == MeasurementQueryKind.Pixel && samples.Length != 1))
            throw new ArgumentException("Coordinates and samples must form valid query pairs.");
        Coordinates = coordinates;
        Samples = Array.AsReadOnly(samples.ToArray());
    }
}
