using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Common provenance for the closed family of immutable measurement query results.</summary>
public abstract class MeasurementQueryResult
{
    /// <summary>Identity of the measurement that produced this result.</summary>
    public Guid MeasurementId { get; }
    /// <summary>Geometry version captured when this query was requested.</summary>
    public long GeometryVersion { get; }
    /// <summary>Identity and layout of the original source frame read by the query.</summary>
    public FrameInfo SourceFrame { get; }
    /// <summary>Query kind that determines the concrete result payload.</summary>
    public MeasurementQueryKind Query { get; }
    private protected MeasurementQueryResult(Guid id, long version, FrameInfo sourceFrame, MeasurementQueryKind query)
    { MeasurementId = id; GeometryVersion = version; SourceFrame = sourceFrame; Query = query; }
}
