using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc />
    public GrayDisplayRange? DisplayRange => _host.Control.DisplayRange;

    /// <inheritdoc />
    public Task SetDisplayRangeAsync(GrayDisplayRange? range) => _host.Control.SetDisplayRangeAsync(range);

    /// <inheritdoc/>
    public void FitToViewport()
    {
        _host.Control.FitToViewport();
    }
}
