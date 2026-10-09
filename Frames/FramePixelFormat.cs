namespace Fizzy.ImageViewer.Frames;

/// <summary>Original pixel storage layout; multibyte grayscale values use little-endian byte order.</summary>
public enum FramePixelFormat
{
    /// <summary>One unsigned 8-bit grayscale channel.</summary>
    Gray8,
    /// <summary>One unsigned 16-bit grayscale channel.</summary>
    Gray16,
    /// <summary>One IEEE 754 32-bit grayscale channel; original values are not limited to the display range.</summary>
    Gray32Float,
    /// <summary>Three 8-bit channels stored in red, green, blue order.</summary>
    Rgb24,
    /// <summary>Three 8-bit channels stored in blue, green, red order.</summary>
    Bgr24,
    /// <summary>Blue, green, red and one unused padding byte; padding is not alpha.</summary>
    Bgr32,
    /// <summary>Blue, green, red and straight alpha, each 8 bits.</summary>
    Bgra32,
    /// <summary>Blue, green, red and alpha, each 8 bits; color values are premultiplied by alpha.</summary>
    Pbgra32
}
