namespace Fizzy.ImageViewer.Frames;

/// <summary>Layout of a top-down, single-plane image. Construction stores values; frame factories validate them.</summary>
/// <param name="Width">Image width in pixels; must be positive.</param>
/// <param name="Height">Image height in pixels; must be positive.</param>
/// <param name="Stride">Byte distance between row starts, at least the packed row size.</param>
/// <param name="Format">Storage format, including channel order and numeric representation.</param>
public readonly record struct FrameDescriptor(int Width, int Height, int Stride, FramePixelFormat Format)
{
    /// <summary>Packed bytes per row, excluding padding. Arithmetic overflow throws.</summary>
    public int RowBytes => checked(Width * Format.BytesPerPixel());
    /// <summary>Minimum buffer size in bytes, excluding padding after the final row. Arithmetic overflow throws.</summary>
    public int RequiredBytes => checked((Height - 1) * Stride + RowBytes);
    /// <summary>Checks positive dimensions, row stride and available buffer length in bytes; throws for invalid layout, insufficient storage or unsupported format.</summary>
    public void Validate(int length)
    {
        if (Width <= 0 || Height <= 0 || Stride < RowBytes)
            throw new ArgumentException("Positive dimensions and a stride at least as large as a row are required.");
        if (length < RequiredBytes) throw new ArgumentException("Frame buffer is too small.");
    }
}
