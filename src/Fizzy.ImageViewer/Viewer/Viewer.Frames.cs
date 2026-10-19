using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc/>
    public event EventHandler<FrameInfo>? FrameCommitted
    {
        add { lock (_host.Control.Runtime.Lifetime.Gate) { _host.Control.Runtime.Lifetime.ThrowIfStopping(); _frameCommitted += value; } }
        remove { lock (_host.Control.Runtime.Lifetime.Gate) _frameCommitted -= value; }
    }
    private EventHandler<FrameInfo>? _frameCommitted;

    /// <inheritdoc cref="IFrameSink.SubmitFrameAsync"/>
    public ValueTask<FrameSubmitResult> SubmitFrameAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken cancellationToken = default)
        => _host.Control.SubmitFrameAsync(frame, options, cancellationToken);

    /// <inheritdoc/>
    public FrameLease? AcquireCurrentFrame() => _host.Control.AcquireCurrentFrame();

    internal void NotifyFrameCommitted(FrameInfo info)
    {
        foreach (EventHandler<FrameInfo> handler in Volatile.Read(ref _frameCommitted)?.GetInvocationList() ?? [])
            try { handler(this, info); }
            catch (Exception ex) { _logger.LogWarning(ex, "Frame notification failed"); }
    }
}
