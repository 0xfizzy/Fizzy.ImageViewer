namespace Fizzy.ImageViewer.Measurements;

/// <summary>Whether a tool click keeps its activation accepting input.</summary>
public enum MeasurementClickResult
{
    /// <summary>Keeps this activation accepting input.</summary>
    Continue,
    /// <summary>Ends this activation; only explicitly completed measurements are retained.</summary>
    Finish
}
