using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Viewport;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <inheritdoc />
    public GrayDisplayRange? DisplayRange => _runtime.Pipeline.DisplayRange;

    /// <inheritdoc />
    public void SetDisplayRange(GrayDisplayRange? range) => InvokeAlive(() => _runtime.Pipeline.SetDisplayRange(range));

    /// <inheritdoc />
    public event EventHandler<DisplayFailure>? DisplayFailed
    {
        add { lock (_runtime.Lifetime.Gate) { _runtime.Lifetime.ThrowIfStopping(); _displayFailed += value; } }
        remove { lock (_runtime.Lifetime.Gate) _displayFailed -= value; }
    }
    private EventHandler<DisplayFailure>? _displayFailed;

    internal void NotifyDisplayFailed(DisplayFailure failure)
    {
        foreach (EventHandler<DisplayFailure> handler in Volatile.Read(ref _displayFailed)?.GetInvocationList() ?? [])
            try { handler(this, failure); }
            catch (Exception ex) { _logger.LogWarning(ex, "Display failure subscriber failed"); }
    }

    /// <inheritdoc/>
    public void FitToViewport()
    {
        InvokeAlive(() => ImageViewport.FitToViewport());
    }
}
