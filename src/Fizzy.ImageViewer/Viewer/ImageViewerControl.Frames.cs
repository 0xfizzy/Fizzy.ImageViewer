using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <inheritdoc/>
    public event EventHandler<FrameInfo>? FrameCommitted
    {
        add { lock (_runtime.Lifetime.Gate) { _runtime.Lifetime.ThrowIfStopping(); _frameCommitted += value; } }
        remove { lock (_runtime.Lifetime.Gate) _frameCommitted -= value; }
    }
    private EventHandler<FrameInfo>? _frameCommitted;

    /// <inheritdoc cref="IFrameSink.SubmitFrameAsync"/>
    public ValueTask<FrameSubmitResult> SubmitFrameAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken cancellationToken = default)
        => _runtime.Pipeline.SubmitAsync(frame, options, cancellationToken);

    /// <inheritdoc/>
    public FrameLease? AcquireCurrentFrame() => _runtime.Pipeline.AcquireCurrentFrame();

    internal void NotifyFrameCommitted(FrameInfo info)
    {
        foreach (EventHandler<FrameInfo> handler in Volatile.Read(ref _frameCommitted)?.GetInvocationList() ?? [])
            try { handler(this, info); }
            catch (Exception ex) { _logger.LogWarning(ex, "Frame notification failed"); }
    }
}
