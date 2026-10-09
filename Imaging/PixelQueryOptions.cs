namespace Fizzy.ImageViewer.Imaging;

/// <summary>Shared query limits. Rates must produce an interval of at least one TimeSpan tick
/// and less than TimeSpan.MaxValue; invalid settings are rejected when applied.</summary>
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
        RateInterval(PixelQueryRateHz, nameof(PixelQueryRateHz));
        RateInterval(LineQueryRateHz, nameof(LineQueryRateHz));
        RateInterval(RegionQueryRateHz, nameof(RegionQueryRateHz));
        if (MaxResultAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MaxResultAge));
    }

    internal static TimeSpan RateInterval(double rate, string parameterName)
    {
        double ticks = TimeSpan.TicksPerSecond / rate;
        // The double representation of long.MaxValue rounds up to 2^63.
        // Reject that boundary before conversion, and round up to respect a maximum rate.
        if (!double.IsFinite(rate) || rate <= 0 || !double.IsFinite(ticks) || ticks < 1 || ticks >= (double)long.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName, "The rate must produce a representable interval of at least one tick.");
        return TimeSpan.FromTicks((long)Math.Ceiling(ticks));
    }
}
