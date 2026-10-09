using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc/>
    public PixelQueryOptions QueryOptions
    {
        get => InvokeAlive(() => _host.Queries.QueryOptions);
        set => InvokeAlive(() => _host.Queries.QueryOptions = value);
    }
    /// <inheritdoc/>
    public PixelQueryMetrics QueryMetrics => InvokeAlive(() => _host.Queries.QueryMetrics);
}
