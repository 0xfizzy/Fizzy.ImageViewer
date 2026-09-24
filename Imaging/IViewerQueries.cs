namespace Fizzy.ImageViewer.Imaging;

/// <summary>Shared pixel-query configuration and metrics for measurements and pixel inspection.</summary>
/// <remarks>Changes affect the viewer-wide scheduler, including queries belonging to other callers.</remarks>
public interface IViewerQueries
{
    /// <summary>测量及像素 HUD 的查询配置，通过查看器 STA 读写。</summary>
    PixelQueryOptions QueryOptions { get; set; }

    /// <summary>内置查询调度器的统计快照，通过查看器 STA 读取。</summary>
    PixelQueryMetrics QueryMetrics { get; }
}
