using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    internal PixelQueryScheduler QueryScheduler => _runtime.Queries;
    public PixelQueryOptions QueryOptions
    {
        get => InvokeAlive(() => _runtime.Queries.QueryOptions);
        set => InvokeAlive(() => _runtime.Queries.QueryOptions = value);
    }
    public PixelQueryMetrics QueryMetrics => InvokeAlive(() => _runtime.Queries.QueryMetrics);
}
