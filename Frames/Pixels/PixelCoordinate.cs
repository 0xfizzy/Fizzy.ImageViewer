namespace Fizzy.ImageViewer.Frames;

/// <summary>Zero-based integer pixel index, not a continuous image position.</summary>
/// <remarks>Pixel (X, Y) covers [X, X+1) by [Y, Y+1) in image coordinates.</remarks>
/// <param name="X">Column index from the left edge.</param>
/// <param name="Y">Row index from the top edge.</param>
public readonly record struct PixelCoordinate(int X, int Y)
{
    /// <summary>Returns this pixel's center in continuous image coordinates, without bounds validation.</summary>
    public System.Windows.Point ToImageCenter() => new(X + 0.5, Y + 0.5);
}
