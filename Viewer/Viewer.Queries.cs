using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private PixelQueryScheduler _queryScheduler = null!;
    internal PixelQueryScheduler QueryScheduler => _queryScheduler;
    public PixelQueryOptions QueryOptions
    {
        get => InvokeAlive(() => _queryScheduler.QueryOptions);
        set => InvokeAlive(() => _queryScheduler.QueryOptions = value);
    }
    public PixelQueryMetrics QueryMetrics => InvokeAlive(() => _queryScheduler.QueryMetrics);
}
