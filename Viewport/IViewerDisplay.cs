using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Viewport;

/// <summary>Image viewport fitting and display mapping, independent of original-pixel queries.</summary>
public interface IViewerDisplay
{
    /// <summary>Centers and scales the full image to fit the viewport. Fit scale may lie outside the ordinary wheel-zoom range.</summary>
    void FitToViewport();

    /// <summary>The requested mapping, which may not yet be committed to the displayed frame.</summary>
    GrayDisplayRange? DisplayRange { get; }

    /// <summary>Sets the requested mapping and waits for a corresponding display commit.
    /// With no current frame, configures future submissions and completes immediately.
    /// A non-null range on a current GPU frame throws synchronously without changing the request.
    /// A later setting or Viewer shutdown cancels a pending update. Preparation or presentation
    /// failure faults the task; the requested value remains set and the previous committed mapping
    /// remains available to snapshots. Success is a UI commit, not physical presentation.</summary>
    Task SetDisplayRangeAsync(GrayDisplayRange? range);
}
