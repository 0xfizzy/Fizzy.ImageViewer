namespace Fizzy.ImageViewer.Frames;

/// <summary>Original pixel values in semantic channel order, without display mapping or unpremultiplication.</summary>
/// <param name="Format">Source storage format.</param>
/// <param name="Gray">Grayscale value in source units; unused for color formats.</param>
/// <param name="R">Red value in 8-bit source units; unused for grayscale.</param>
/// <param name="G">Green value in 8-bit source units; unused for grayscale.</param>
/// <param name="B">Blue value in 8-bit source units; unused for grayscale.</param>
/// <param name="A">Alpha in 8-bit source units; opaque formats use 255.</param>
public readonly record struct PixelSample(FramePixelFormat Format, double Gray, double R, double G, double B, double A)
{
    /// <summary>Whether the format has one grayscale channel.</summary>
    public bool IsGrayscale => Format.GetInfo().IsGrayscale;
    /// <summary>Whether the stored color channels are premultiplied by alpha.</summary>
    public bool IsPremultiplied => Format.GetInfo().IsPremultiplied;
    /// <summary>Formats grayscale or semantic RGBA values, marking premultiplied color samples.</summary>
    public override string ToString() => IsGrayscale ? $"GRAY: {Gray:G7}" :
        $"R: {R:G7}, G: {G:G7}, B: {B:G7}, A: {A:G7}{(IsPremultiplied ? " (premultiplied)" : "")}";
}
