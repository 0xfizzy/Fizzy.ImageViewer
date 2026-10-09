using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc />
    public GrayDisplayRange? DisplayRange => _host.Pipeline.DisplayRange;

    /// <inheritdoc />
    public Task SetDisplayRangeAsync(GrayDisplayRange? range) => _host.Pipeline.SetDisplayRangeAsync(range);

    /// <inheritdoc/>
    public void FitToViewport()
    {
        InvokeAlive(() => _host.Window.ImageViewport.FitToViewport());
    }
}
