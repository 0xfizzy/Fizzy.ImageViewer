namespace Fizzy.ImageViewer.Measurements;

public sealed record MeasurementOptions
{
    public MeasurementQueryKind Query { get; init; }
    /// <summary>Opens a viewer-owned plot for a line-profile query when the measurement completes.</summary>
    public bool ShowProfileWindow { get; init; }
    public MeasurementStyle? Style { get; init; }

    internal void Validate(MeasurementGeometry geometry)
    {
        MeasurementQueryDefinition.Validate(Query, geometry);
        if (ShowProfileWindow && Query != MeasurementQueryKind.LineProfile)
            throw new ArgumentException("A profile window requires a line-profile query.", nameof(ShowProfileWindow));
    }
}
