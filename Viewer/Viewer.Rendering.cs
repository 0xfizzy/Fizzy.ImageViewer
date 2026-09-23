using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Internal;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private FramePipeline _pipeline = null!;
    private FramePresentation _presentation = null!;
    public event Action<FrameInfo>? FrameCommitted;

    public ValueTask<FrameSubmitResult> SubmitFrameAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken ct = default)
        => _pipeline.SubmitAsync(frame, options, ct);

    public FrameLease? AcquireCurrentFrame() => _pipeline.AcquireCurrentFrame();
    private FrameLease? AcquireCurrentFrameForMeasurement() => _pipeline.AcquireCurrentFrameForMeasurement();

    public GrayDisplayRange? DisplayRange
    {
        get => _pipeline.DisplayRange;
        set => _pipeline.DisplayRange = value;
    }

    private void NotifyFrameCommitted(FrameLease frame, FrameSubmissionOptions? options)
    {
        Notify(() => { using var borrowed = frame.Acquire(); options?.OnCommitted?.Invoke(borrowed); });
        Notify(() => _measureManager?.NotifyFrameCommitted(frame.Info));
        foreach (Action<FrameInfo> handler in FrameCommitted?.GetInvocationList() ?? [])
            Notify(() => handler(frame.Info));
    }

    private void Notify(Action action)
    {
        try { action(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Frame notification failed"); }
    }
}
