using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Viewport;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc />
    public GrayDisplayRange? DisplayRange => _host.Control.DisplayRange;

    /// <inheritdoc />
    public void SetDisplayRange(GrayDisplayRange? range) => _host.Control.SetDisplayRange(range);

    /// <inheritdoc />
    public event EventHandler<DisplayFailure>? DisplayFailed
    {
        add { lock (_host.Control.Runtime.Lifetime.Gate) { _host.Control.Runtime.Lifetime.ThrowIfStopping(); _displayFailed += value; } }
        remove { lock (_host.Control.Runtime.Lifetime.Gate) _displayFailed -= value; }
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
        _host.Control.FitToViewport();
    }
}
