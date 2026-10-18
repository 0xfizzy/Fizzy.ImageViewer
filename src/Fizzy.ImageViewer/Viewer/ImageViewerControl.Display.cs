using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <inheritdoc />
    public GrayDisplayRange? DisplayRange => _runtime.Pipeline.DisplayRange;

    /// <inheritdoc />
    public Task SetDisplayRangeAsync(GrayDisplayRange? range) => _runtime.Pipeline.SetDisplayRangeAsync(range);

    /// <inheritdoc/>
    public void FitToViewport()
    {
        InvokeAlive(() => ImageViewport.FitToViewport());
    }
}
