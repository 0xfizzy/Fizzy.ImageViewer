namespace Fizzy.ImageViewer.Measurements;

/// <summary>Pixel query attached to a compatible measurement geometry.</summary>
public enum MeasurementQueryKind
{
    /// <summary>No pixel query; supported by all geometries.</summary>
    None,
    /// <summary>One original pixel for point or crosshair geometry.</summary>
    Pixel,
    /// <summary>Ordered original-pixel samples along line geometry.</summary>
    LineProfile,
    /// <summary>Per-channel original-value statistics over clipped rectangle geometry.</summary>
    RegionStatistics
}
