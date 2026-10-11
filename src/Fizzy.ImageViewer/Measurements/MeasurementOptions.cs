namespace Fizzy.ImageViewer.Measurements;

/// <summary>Query and appearance options validated when a measurement is created.</summary>
public sealed record MeasurementOptions
{
    /// <summary>Requested pixel query; defaults to None. The query must support the supplied geometry.</summary>
    public MeasurementQueryKind Query { get; init; }
    /// <summary>Opens a viewer-owned plot for a line-profile query when the measurement completes.</summary>
    public bool ShowProfileWindow { get; init; }
    /// <summary>Optional appearance override, snapshotted at creation; null uses the viewer style.</summary>
    public MeasurementStyle? Style { get; init; }

    internal void Validate(MeasurementGeometry geometry)
    {
        MeasurementQueryDefinition.Validate(Query, geometry);
        if (ShowProfileWindow && Query != MeasurementQueryKind.LineProfile)
            throw new ArgumentException("A profile window requires a line-profile query.", nameof(ShowProfileWindow));
    }
}
