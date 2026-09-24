namespace Fizzy.ImageViewer.Imaging;

public sealed record PixelQueryOptions
{
    public double PixelRate { get; init; } = 30;
    public double LineRate { get; init; } = 30;
    public double RegionRate { get; init; } = 10;
    public TimeSpan MaxResultAge { get; init; } = TimeSpan.FromMilliseconds(100);
    internal void Validate()
    {
        if (!double.IsFinite(PixelRate) || PixelRate<=0 || !double.IsFinite(LineRate) || LineRate<=0 || !double.IsFinite(RegionRate) || RegionRate<=0 || MaxResultAge<=TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(PixelQueryOptions));
    }
}
