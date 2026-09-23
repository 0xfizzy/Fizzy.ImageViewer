using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public static PixelRegion Full(FrameDescriptor d) => new(0, 0, d.Width, d.Height);
    public void Validate(FrameDescriptor d)
    {
        if (IsEmpty || X < 0 || Y < 0 || (long)X + Width > d.Width || (long)Y + Height > d.Height)
            throw new ArgumentOutOfRangeException(nameof(PixelRegion));
    }
    public static PixelRegion Clip(double x, double y, double width, double height, FrameDescriptor d)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return default;
        int left = (int)Math.Clamp(Math.Floor(x), 0, d.Width), top = (int)Math.Clamp(Math.Floor(y), 0, d.Height);
        int right = (int)Math.Clamp(Math.Ceiling(x + width), 0, d.Width), bottom = (int)Math.Clamp(Math.Ceiling(y + height), 0, d.Height);
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
