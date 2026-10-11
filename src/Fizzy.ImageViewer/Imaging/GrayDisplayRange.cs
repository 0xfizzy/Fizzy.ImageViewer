namespace Fizzy.ImageViewer.Imaging;

/// <summary>Grayscale display mapping in original-value units; values outside the bounds are clipped to black or white.</summary>
public readonly record struct GrayDisplayRange
{
    /// <summary>Original value mapped to black.</summary>
    public double Minimum { get; }
    /// <summary>Original value mapped to white.</summary>
    public double Maximum { get; }
    /// <summary>Creates a mapping with finite, increasing bounds and a finite difference; otherwise throws ArgumentOutOfRangeException.</summary>
    public GrayDisplayRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum || !double.IsFinite(maximum - minimum))
            throw new ArgumentOutOfRangeException(nameof(maximum));
        Minimum = minimum;
        Maximum = maximum;
    }
}
