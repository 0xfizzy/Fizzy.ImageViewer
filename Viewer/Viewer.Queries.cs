using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    internal PixelQueryScheduler QueryScheduler => _host.Queries;
    public PixelQueryOptions QueryOptions
    {
        get => InvokeAlive(() => _host.Queries.QueryOptions);
        set => InvokeAlive(() => _host.Queries.QueryOptions = value);
    }
    public PixelQueryMetrics QueryMetrics => InvokeAlive(() => _host.Queries.QueryMetrics);
}
