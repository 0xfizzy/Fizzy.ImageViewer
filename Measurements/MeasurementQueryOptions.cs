namespace Fizzy.ImageViewer.Measurements;

/// <summary>Closed query configuration; presentation options belong to their query.</summary>
public abstract class MeasurementQueryOptions
{
    private protected MeasurementQueryOptions(MeasurementQuery kind) => Kind = kind;
    public MeasurementQuery Kind { get; }
    public static MeasurementQueryOptions None { get; } = new FixedQueryOptions(MeasurementQuery.None);
    public static MeasurementQueryOptions Pixel { get; } = new FixedQueryOptions(MeasurementQuery.Pixel);
    public static LineProfileMeasurementQueryOptions LineProfile { get; } = new();
    public static MeasurementQueryOptions RegionStatistics { get; } = new FixedQueryOptions(MeasurementQuery.RegionStatistics);

    private sealed class FixedQueryOptions(MeasurementQuery kind) : MeasurementQueryOptions(kind);
}
