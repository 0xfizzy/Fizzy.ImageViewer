using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public event Action<FrameInfo>? FrameCommitted;

    public ValueTask<FrameSubmitResult> SubmitFrameAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken ct = default)
        => _host.Pipeline.SubmitAsync(frame, options, ct);

    public FrameLease? AcquireCurrentFrame() => _host.Pipeline.AcquireCurrentFrame();

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
