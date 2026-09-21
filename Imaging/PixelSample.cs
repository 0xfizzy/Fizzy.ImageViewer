using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

public readonly record struct PixelSample(FramePixelFormat Format, double Gray, double R, double G, double B, double A)
{
    public bool IsGrayscale => Format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float;
    public bool IsPremultiplied => Format == FramePixelFormat.Pbgra32;
    public override string ToString() => IsGrayscale ? $"GRAY: {Gray:G7}" :
        $"R: {R:G7}, G: {G:G7}, B: {B:G7}, A: {A:G7}{(IsPremultiplied ? " (premultiplied)" : "")}";
}
