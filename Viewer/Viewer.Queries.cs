using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public PixelQueryOptions QueryOptions
    {
        get => InvokeAlive(() => _host.Queries.QueryOptions);
        set => InvokeAlive(() => _host.Queries.QueryOptions = value);
    }
    public PixelQueryMetrics QueryMetrics => InvokeAlive(() => _host.Queries.QueryMetrics);
}
