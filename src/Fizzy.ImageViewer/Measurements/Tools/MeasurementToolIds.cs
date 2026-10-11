namespace Fizzy.ImageViewer.Measurements;

/// <summary>Stable IDs independent of menu labels.</summary>
public static class MeasurementToolIds
{
    /// <summary>Built-in point tool with an original-pixel query.</summary>
    public const string Point = "Point";
    /// <summary>Built-in line-length tool without a pixel query.</summary>
    public const string Length = "Length";
    /// <summary>Built-in rectangle tool with region statistics.</summary>
    public const string RectangleRoi = "RectangleRoi";
    /// <summary>Built-in line-profile tool with an owned plot window.</summary>
    public const string LineProfile = "LineProfile";
}
