namespace Fizzy.ImageViewer.Measurements;

/// <summary>Shape of a measurement, independent of its originating tool or pixel query.</summary>
public enum MeasurementGeometryKind
{
    /// <summary>A point marker.</summary>
    Point,
    /// <summary>A crosshair marker.</summary>
    Crosshair,
    /// <summary>An ordered line segment.</summary>
    Line,
    /// <summary>An axis-aligned rectangle.</summary>
    Rectangle,
    /// <summary>A circle.</summary>
    Circle
}
