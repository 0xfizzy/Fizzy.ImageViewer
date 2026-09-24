using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Common provenance for the closed family of immutable measurement query results.</summary>
public abstract class MeasurementResult
{
    public Guid MeasurementId { get; }
    public long GeometryVersion { get; }
    public FrameInfo Frame { get; }
    public MeasurementQuery Query { get; }
    private protected MeasurementResult(Guid id, long version, FrameInfo frame, MeasurementQuery query)
    { MeasurementId = id; GeometryVersion = version; Frame = frame; Query = query; }
}
