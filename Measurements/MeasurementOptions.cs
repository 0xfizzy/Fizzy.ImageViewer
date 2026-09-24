namespace Fizzy.ImageViewer.Measurements;

public sealed record MeasurementOptions
{
    public MeasurementQueryOptions Query { get; init; } = MeasurementQueryOptions.None;
    public MeasurementStyle? Style { get; init; }

    internal void Validate(MeasurementGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(Query);
        bool valid = Query.Kind switch
        {
            MeasurementQuery.None => true,
            MeasurementQuery.Pixel => geometry.Kind is MeasurementGeometryKind.Point or MeasurementGeometryKind.Crosshair,
            MeasurementQuery.LineProfile => geometry.Kind == MeasurementGeometryKind.Line,
            MeasurementQuery.RegionStatistics => geometry.Kind == MeasurementGeometryKind.Rectangle,
            _ => false
        };
        if (!valid)
            throw new ArgumentException("The query must match the measurement geometry.");
    }
}
