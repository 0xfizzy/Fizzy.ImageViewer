namespace Fizzy.ImageViewer.Frames;

public static class FramePixelFormats
{
    private static readonly FramePixelFormatInfo Gray8 = new(FramePixelFormatInfo.ComponentOrder.Gray, 8, FramePixelFormatInfo.NumericKind.UnsignedInteger);
    private static readonly FramePixelFormatInfo Gray16 = new(FramePixelFormatInfo.ComponentOrder.Gray, 16, FramePixelFormatInfo.NumericKind.UnsignedInteger);
    private static readonly FramePixelFormatInfo Gray32Float = new(FramePixelFormatInfo.ComponentOrder.Gray, 32, FramePixelFormatInfo.NumericKind.FloatingPoint);
    private static readonly FramePixelFormatInfo Rgb24 = new(FramePixelFormatInfo.ComponentOrder.Rgb, 8, FramePixelFormatInfo.NumericKind.UnsignedInteger);
    private static readonly FramePixelFormatInfo Bgr24 = new(FramePixelFormatInfo.ComponentOrder.Bgr, 8, FramePixelFormatInfo.NumericKind.UnsignedInteger);
    private static readonly FramePixelFormatInfo Bgr32 = new(FramePixelFormatInfo.ComponentOrder.Bgrx, 8, FramePixelFormatInfo.NumericKind.UnsignedInteger);
    private static readonly FramePixelFormatInfo Bgra32 = new(FramePixelFormatInfo.ComponentOrder.Bgra, 8, FramePixelFormatInfo.NumericKind.UnsignedInteger, FramePixelFormatInfo.AlphaKind.Straight);
    private static readonly FramePixelFormatInfo Pbgra32 = new(FramePixelFormatInfo.ComponentOrder.Bgra, 8, FramePixelFormatInfo.NumericKind.UnsignedInteger, FramePixelFormatInfo.AlphaKind.Premultiplied);

    public static int BytesPerPixel(this FramePixelFormat format) => format.GetInfo().BytesPerPixel;

    internal static FramePixelFormatInfo GetInfo(this FramePixelFormat format) => format switch
    {
        FramePixelFormat.Gray8 => Gray8,
        FramePixelFormat.Gray16 => Gray16,
        FramePixelFormat.Gray32Float => Gray32Float,
        FramePixelFormat.Rgb24 => Rgb24,
        FramePixelFormat.Bgr24 => Bgr24,
        FramePixelFormat.Bgr32 => Bgr32,
        FramePixelFormat.Bgra32 => Bgra32,
        FramePixelFormat.Pbgra32 => Pbgra32,
        _ => throw new NotSupportedException($"Unsupported pixel format: {format}")
    };
}
