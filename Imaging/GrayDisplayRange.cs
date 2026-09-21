namespace Fizzy.ImageViewer.Imaging;

public readonly record struct GrayDisplayRange
{
    public double Minimum { get; }
    public double Maximum { get; }
    public GrayDisplayRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum || !double.IsFinite(maximum - minimum))
            throw new ArgumentOutOfRangeException(nameof(maximum));
        Minimum = minimum;
        Maximum = maximum;
    }
}
