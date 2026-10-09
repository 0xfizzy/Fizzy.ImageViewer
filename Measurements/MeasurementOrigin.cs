namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable tool registration ID and unique activation ID captured at creation.</summary>
/// <param name="ToolId">Case-sensitive ID of the originating tool registration.</param>
/// <param name="SessionId">Unique activation identity shared by measurements from the same session.</param>
public sealed record MeasurementOrigin(string ToolId, Guid SessionId);
