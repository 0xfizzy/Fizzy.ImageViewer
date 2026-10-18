using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <inheritdoc/>
    public event Action<FrameInfo>? FrameCommitted;

    /// <inheritdoc cref="IFrameSink.SubmitFrameAsync"/>
    public ValueTask<FrameSubmitResult> SubmitFrameAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken cancellationToken = default)
        => _runtime.Pipeline.SubmitAsync(frame, options, cancellationToken);

    /// <inheritdoc/>
    public FrameLease? AcquireCurrentFrame() => _runtime.Pipeline.AcquireCurrentFrame();

    internal void NotifyFrameCommitted(FrameInfo info)
    {
        foreach (Action<FrameInfo> handler in FrameCommitted?.GetInvocationList() ?? [])
            Notify(() => handler(info));
    }

    private void Notify(Action action)
    {
        try { action(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Frame notification failed"); }
    }
}
