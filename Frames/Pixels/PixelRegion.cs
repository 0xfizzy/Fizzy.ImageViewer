namespace Fizzy.ImageViewer.Frames;

/// <summary>Integer image region with exclusive right and bottom edges.</summary>
/// <param name="X">Left column index.</param>
/// <param name="Y">Top row index.</param>
/// <param name="Width">Number of columns.</param>
/// <param name="Height">Number of rows.</param>
public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    /// <summary>Whether either dimension is zero or negative.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
    /// <summary>Returns the entire image extent described by the supplied frame layout.</summary>
    public static PixelRegion Full(FrameDescriptor d) => new(0, 0, d.Width, d.Height);
    /// <summary>Throws ArgumentOutOfRangeException unless this region is nonempty and wholly inside the supplied frame.</summary>
    public void Validate(FrameDescriptor d)
    {
        if (IsEmpty || X < 0 || Y < 0 || (long)X + Width > d.Width || (long)Y + Height > d.Height)
            throw new ArgumentOutOfRangeException(nameof(PixelRegion));
    }
    /// <summary>Rounds left/top down and right/bottom up, then intersects with the frame. Non-finite inputs or nonpositive dimensions yield an empty region.</summary>
    public static PixelRegion Clip(double x, double y, double width, double height, FrameDescriptor d)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return default;
        int left = (int)Math.Clamp(Math.Floor(x), 0, d.Width), top = (int)Math.Clamp(Math.Floor(y), 0, d.Height);
        int right = (int)Math.Clamp(Math.Ceiling(x + width), 0, d.Width), bottom = (int)Math.Clamp(Math.Ceiling(y + height), 0, d.Height);
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
