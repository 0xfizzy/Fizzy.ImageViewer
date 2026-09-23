using Fizzy.ImageViewer.Drawing;

namespace Fizzy.ImageViewer.Measurements;

public enum MeasurementQuery { None, Pixel, LineProfile, RegionStatistics }

public sealed record MeasurementOptions
{
    public MeasurementQuery Query { get; init; }
    public bool ShowLineProfile { get; init; }
    public ShapeStyle? Style { get; init; }

    internal void Validate(MeasurementGeometry geometry)
    {
        bool valid = Query switch
        {
            MeasurementQuery.None => true,
            MeasurementQuery.Pixel => geometry.Kind is ShapeType.Point or ShapeType.Crosshair,
            MeasurementQuery.LineProfile => geometry.Kind == ShapeType.Line,
            MeasurementQuery.RegionStatistics => geometry.Kind == ShapeType.Rectangle,
            _ => false
        };
        if (!valid || (ShowLineProfile && Query != MeasurementQuery.LineProfile))
            throw new ArgumentException("The query and presentation must match the measurement geometry.");
    }
}
