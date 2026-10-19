using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Viewport;

/// <summary>Image viewport fitting and display mapping, independent of original-pixel queries.</summary>
public interface IViewerDisplay
{
    /// <summary>Centers and scales the full image to fit the viewport. Fit scale may lie outside the ordinary wheel-zoom range.</summary>
    void FitToViewport();

    /// <summary>The requested mapping, which may not yet be committed to the displayed frame.</summary>
    GrayDisplayRange? DisplayRange { get; }

    /// <summary>Accepts a mapping on the viewer STA and schedules display when visible; does not wait for display.</summary>
    /// <remarks>A non-null range on a current GPU frame is rejected before changing the request.
    /// Snapshots retain the last successfully committed mapping. Failures are reported by DisplayFailed.</remarks>
    void SetDisplayRange(GrayDisplayRange? range);

    /// <summary>Reports failed frame presentation or redraw on the viewer STA, with this public owner as sender.</summary>
    /// <remarks>Does not report normal replacement or hiding. Subscribe before submitting frames.
    /// Adding rejects shutdown; removal is idempotent. Subscriber exceptions are isolated.
    /// Failures are not replayed and do not alter completed reception results.</remarks>
    event EventHandler<DisplayFailure>? DisplayFailed;
}
