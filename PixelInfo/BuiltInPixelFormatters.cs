using System.Windows.Media;

namespace Fizzy.ImageViewer.PixelInfo;

public sealed class Gray8Formatter : IPixelFormatter
{
    public static readonly Gray8Formatter Instance = new();
    private Gray8Formatter() { }

    public bool CanFormat(PixelFormat format) => format == PixelFormats.Gray8;

    public bool TryRead(ReadOnlySpan<byte> pixelData, out PixelValue value)
    {
        if (pixelData.Length < 1)
        {
            value = default;
            return false;
        }

        value = new PixelValue(pixelData[0]);
        return true;
    }
}

public sealed class Rgb24Formatter : IPixelFormatter
{
    public static readonly Rgb24Formatter Instance = new();
    private Rgb24Formatter() { }

    public bool CanFormat(PixelFormat format) => format == PixelFormats.Rgb24;

    public bool TryRead(ReadOnlySpan<byte> pixelData, out PixelValue value)
    {
        if (pixelData.Length < 3)
        {
            value = default;
            return false;
        }

        value = new PixelValue(pixelData[0], pixelData[1], pixelData[2]);
        return true;
    }
}

public sealed class Bgr24Formatter : IPixelFormatter
{
    public static readonly Bgr24Formatter Instance = new();
    private Bgr24Formatter() { }

    public bool CanFormat(PixelFormat format) => format == PixelFormats.Bgr24;

    public bool TryRead(ReadOnlySpan<byte> pixelData, out PixelValue value)
    {
        if (pixelData.Length < 3)
        {
            value = default;
            return false;
        }

        value = new PixelValue(pixelData[2], pixelData[1], pixelData[0]);
        return true;
    }
}

public sealed class Bgr32Formatter : IPixelFormatter
{
    public static readonly Bgr32Formatter Instance = new();
    private Bgr32Formatter() { }

    public bool CanFormat(PixelFormat format) =>
        format == PixelFormats.Bgr32 ||
        format == PixelFormats.Bgra32 ||
        format == PixelFormats.Pbgra32;

    public bool TryRead(ReadOnlySpan<byte> pixelData, out PixelValue value)
    {
        if (pixelData.Length < 3)
        {
            value = default;
            return false;
        }

        value = new PixelValue(pixelData[2], pixelData[1], pixelData[0]);
        return true;
    }
}
