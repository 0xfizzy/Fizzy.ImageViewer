namespace Fizzy.ImageViewer.Measurements;

public sealed record MeasurementOptions
{
    public MeasurementQuery Query { get; init; }
    public bool ShowProfileWindow { get; init; }
    public MeasurementStyle? Style { get; init; }

    internal void Validate(MeasurementGeometry geometry)
    {
        bool valid = Query switch
        {
            MeasurementQuery.None => true,
            MeasurementQuery.Pixel => geometry.Kind is MeasurementKind.Point or MeasurementKind.Crosshair,
            MeasurementQuery.LineProfile => geometry.Kind == MeasurementKind.Line,
            MeasurementQuery.RegionStatistics => geometry.Kind == MeasurementKind.Rectangle,
            _ => false
        };
        if (!valid || (ShowProfileWindow && Query != MeasurementQuery.LineProfile))
            throw new ArgumentException("The query and presentation must match the measurement geometry.");
    }
}
