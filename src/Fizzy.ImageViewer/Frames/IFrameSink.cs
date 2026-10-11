namespace Fizzy.ImageViewer.Frames;

/// <summary>Frame submission with immediate ownership transfer, including cancellation, rejection and failure.</summary>
public interface IFrameSink
{
    /// <summary>Consumes the frame immediately, including when cancelled, frozen or closed.
    /// Awaiting reports commit/drop completion, not physical presentation. A frame cannot be submitted twice.</summary>
    /// <remarks>The sink owns release on every terminal path. Callers must not mutate or release transferred storage.
    /// Inspect <see cref="FrameSubmitResult.Error"/> for every status: submission-owned cleanup may fail even when
    /// the status is not <see cref="FrameSubmitStatus.Failed"/>. The status preserves the actual commit/drop outcome;
    /// a cleanup failure does not undo a commit.</remarks>
    ValueTask<FrameSubmitResult> SubmitFrameAsync(
        ImageFrame frame,
        FrameSubmissionOptions? options = null,
        CancellationToken cancellationToken = default);
}
