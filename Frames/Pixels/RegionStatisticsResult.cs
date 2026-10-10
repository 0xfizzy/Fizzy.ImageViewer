namespace Fizzy.ImageViewer.Frames;

/// <summary>Immutable region statistics with source provenance.</summary>
/// <param name="SourceFrame">Identity and layout of the queried source frame.</param>
/// <param name="Region">Queried region in source coordinates.</param>
/// <param name="Statistics">Original-value statistics in semantic channel order.</param>
public sealed record RegionStatisticsResult(FrameInfo SourceFrame, PixelRegion Region, RegionStatistics Statistics);
