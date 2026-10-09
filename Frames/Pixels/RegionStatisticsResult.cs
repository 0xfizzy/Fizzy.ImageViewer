namespace Fizzy.ImageViewer.Frames;

/// <summary>Immutable region statistics with source provenance.</summary>
/// <param name="Frame">Identity and layout of the queried frame.</param>
/// <param name="Region">Queried region in source coordinates.</param>
/// <param name="Statistics">Original-value statistics in semantic channel order.</param>
public sealed record RegionStatisticsResult(FrameInfo Frame, PixelRegion Region, RegionStatistics Statistics);
