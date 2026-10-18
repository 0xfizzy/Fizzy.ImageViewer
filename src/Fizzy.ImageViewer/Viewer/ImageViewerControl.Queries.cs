using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <inheritdoc/>
    public PixelQueryOptions QueryOptions
    {
        get => InvokeAlive(() => _runtime.Queries.QueryOptions);
        set => InvokeAlive(() => _runtime.Queries.QueryOptions = value);
    }
    /// <inheritdoc/>
    public PixelQueryMetrics QueryMetrics => InvokeAlive(() => _runtime.Queries.QueryMetrics);
}
