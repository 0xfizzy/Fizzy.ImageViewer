namespace Fizzy.ImageViewer.Frames;

/// <summary>Frame submission with immediate ownership transfer, including cancellation, rejection and failure.</summary>
public interface IFrameSink
{
    /// <summary>Consumes the frame immediately, including rejection. Completion reports reception, not display.</summary>
    /// <remarks>Accepted frames may be replaced without display. Cancellation applies only before acceptance.
    /// Callers must not mutate or release transferred storage; receipt does not make its buffer reusable.
    /// Inspect FrameSubmitResult.Error for rejection cleanup failures. Later owned cleanup failures enter disposal.
    /// Observe display through OnCommitted, FrameCommitted and DisplayFailed.</remarks>
    ValueTask<FrameSubmitResult> SubmitFrameAsync(
        ImageFrame frame,
        FrameSubmissionOptions? options = null,
        CancellationToken cancellationToken = default);
}
