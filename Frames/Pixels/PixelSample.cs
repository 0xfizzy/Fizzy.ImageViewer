namespace Fizzy.ImageViewer.Frames;

public readonly record struct PixelSample(FramePixelFormat Format, double Gray, double R, double G, double B, double A)
{
    public bool IsGrayscale => Format.GetInfo().IsGrayscale;
    public bool IsPremultiplied => Format.GetInfo().IsPremultiplied;
    public override string ToString() => IsGrayscale ? $"GRAY: {Gray:G7}" :
        $"R: {R:G7}, G: {G:G7}, B: {B:G7}, A: {A:G7}{(IsPremultiplied ? " (premultiplied)" : "")}";
}
