using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public event Action<FrameInfo>? FrameCommitted;

    public ValueTask<FrameSubmitResult> SubmitFrameAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken ct = default)
        => _host.Pipeline.SubmitAsync(frame, options, ct);

    public FrameLease? AcquireCurrentFrame() => _host.Pipeline.AcquireCurrentFrame();

    public GrayDisplayRange? DisplayRange
    {
        get => _host.Pipeline.DisplayRange;
        set => _host.Pipeline.DisplayRange = value;
    }

    internal void NotifyFrameCommitted(FrameLease frame, FrameSubmissionOptions? options)
    {
        Notify(() => { using var borrowed = frame.Acquire(); options?.OnCommitted?.Invoke(borrowed); });
        Notify(() => _host.Measurements?.NotifyFrameCommitted(frame.Info));
        foreach (Action<FrameInfo> handler in FrameCommitted?.GetInvocationList() ?? [])
            Notify(() => handler(frame.Info));
    }

    private void Notify(Action action)
    {
        try { action(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Frame notification failed"); }
    }
}
