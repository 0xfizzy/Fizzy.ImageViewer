namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable tool registration ID and unique activation ID captured at creation.</summary>
public sealed record MeasurementOrigin(string ToolId, Guid SessionId);
