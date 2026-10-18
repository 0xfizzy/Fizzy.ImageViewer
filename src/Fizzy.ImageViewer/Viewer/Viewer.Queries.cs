using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc/>
    public PixelQueryOptions QueryOptions
    {
        get => _host.Control.QueryOptions;
        set => _host.Control.QueryOptions = value;
    }
    /// <inheritdoc/>
    public PixelQueryMetrics QueryMetrics => _host.Control.QueryMetrics;
}
