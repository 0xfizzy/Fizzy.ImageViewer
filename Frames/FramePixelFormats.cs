namespace Fizzy.ImageViewer.Frames;

public static class FramePixelFormats
{
    public static int BytesPerPixel(this FramePixelFormat format) => format switch
    {
        FramePixelFormat.Gray8 => 1,
        FramePixelFormat.Gray16 => 2,
        FramePixelFormat.Rgb24 or FramePixelFormat.Bgr24 => 3,
        FramePixelFormat.Gray32Float or FramePixelFormat.Bgr32 or FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32 => 4,
        _ => throw new NotSupportedException($"Unsupported pixel format: {format}")
    };
}
