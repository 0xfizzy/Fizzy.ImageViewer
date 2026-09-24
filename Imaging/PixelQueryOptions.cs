namespace Fizzy.ImageViewer.Imaging;

public sealed record PixelQueryOptions
{
    /// <summary>Maximum query frequency in hertz for each point-pixel client.</summary>
    public double PixelQueryRateHz { get; init; } = 30;
    /// <summary>Maximum query frequency in hertz for each line-profile client.</summary>
    public double LineQueryRateHz { get; init; } = 30;
    /// <summary>Maximum query frequency in hertz for each region-statistics client.</summary>
    public double RegionQueryRateHz { get; init; } = 10;
    public TimeSpan MaxResultAge { get; init; } = TimeSpan.FromMilliseconds(100);
    internal void Validate()
    {
        if (!double.IsFinite(PixelQueryRateHz) || PixelQueryRateHz<=0 || !double.IsFinite(LineQueryRateHz) || LineQueryRateHz<=0 || !double.IsFinite(RegionQueryRateHz) || RegionQueryRateHz<=0 || MaxResultAge<=TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(PixelQueryOptions));
    }
}
