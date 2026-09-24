using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Common provenance for the closed family of immutable measurement query results.</summary>
public abstract class MeasurementQueryResult
{
    public Guid MeasurementId { get; }
    public long GeometryVersion { get; }
    public FrameInfo Frame { get; }
    public MeasurementQueryKind Query { get; }
    private protected MeasurementQueryResult(Guid id, long version, FrameInfo frame, MeasurementQueryKind query)
    { MeasurementId = id; GeometryVersion = version; Frame = frame; Query = query; }
}
