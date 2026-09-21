namespace Fizzy.ImageViewer.Frames;

public readonly record struct FrameDescriptor(int Width, int Height, int Stride, FramePixelFormat Format)
{
    public int RowBytes => checked(Width * Format.BytesPerPixel());
    public int RequiredBytes => checked((Height - 1) * Stride + RowBytes);
    public void Validate(int length)
    {
        if (Width <= 0 || Height <= 0 || Stride < RowBytes)
            throw new ArgumentException("Positive dimensions and a stride at least as large as a row are required.");
        if (length < RequiredBytes) throw new ArgumentException("Frame buffer is too small.");
    }
}
