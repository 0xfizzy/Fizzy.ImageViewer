namespace Fizzy.ImageViewer.Imaging;

/// <summary>Shared pixel-query configuration and metrics for measurements and pixel inspection.</summary>
/// <remarks>Changes affect the viewer-wide scheduler, including queries belonging to other callers.</remarks>
public interface IViewerQueries
{
    /// <summary>Query configuration shared by measurements and pixel HUD; access dispatches to the viewer STA.</summary>
    PixelQueryOptions QueryOptions { get; set; }

    /// <summary>Statistics snapshot of the shared query scheduler, read on the viewer STA.</summary>
    PixelQueryMetrics QueryMetrics { get; }
}
