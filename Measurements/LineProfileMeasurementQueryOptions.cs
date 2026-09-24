namespace Fizzy.ImageViewer.Measurements;

/// <summary>Line samples with an optional viewer-owned profile window.</summary>
public sealed class LineProfileMeasurementQueryOptions(bool showWindow = false)
    : MeasurementQueryOptions(MeasurementQuery.LineProfile)
{
    public bool ShowWindow { get; } = showWindow;
}
