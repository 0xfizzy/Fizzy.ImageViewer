using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Viewport;

/// <summary>Image viewport fitting and display mapping, independent of original-pixel queries.</summary>
public interface IViewerDisplay
{
    void FitToViewport();

    GrayDisplayRange? DisplayRange { get; set; }
}
