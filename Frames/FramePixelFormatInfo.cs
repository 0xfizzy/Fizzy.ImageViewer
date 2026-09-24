namespace Fizzy.ImageViewer.Frames;

/// <summary>Intrinsic layout of the supported interleaved, little-endian pixel formats.</summary>
internal sealed record FramePixelFormatInfo(
    FramePixelFormatInfo.ComponentOrder Order,
    int BitsPerComponent,
    FramePixelFormatInfo.NumericKind NumericType,
    FramePixelFormatInfo.AlphaKind Alpha = FramePixelFormatInfo.AlphaKind.None)
{
    internal enum ComponentOrder { Gray, Rgb, Bgr, Bgrx, Bgra }
    internal enum NumericKind { UnsignedInteger, FloatingPoint }
    internal enum AlphaKind { None, Straight, Premultiplied }

    internal bool IsGrayscale => Order == ComponentOrder.Gray;
    internal bool HasAlpha => Alpha != AlphaKind.None;
    internal bool IsPremultiplied => Alpha == AlphaKind.Premultiplied;
    internal int SemanticChannelCount => IsGrayscale ? 1 : HasAlpha ? 4 : 3;
    internal int BytesPerPixel => (Order is ComponentOrder.Bgrx or ComponentOrder.Bgra ? 4 : SemanticChannelCount) * (BitsPerComponent / 8);
}
