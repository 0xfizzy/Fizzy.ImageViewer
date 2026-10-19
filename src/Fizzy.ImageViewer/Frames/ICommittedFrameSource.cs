namespace Fizzy.ImageViewer.Frames;

/// <summary>Access to committed original frames and commit notifications.</summary>
public interface ICommittedFrameSource
{
    /// <summary>Acquires an independent lease on the current committed frame, or null when none exists.
    /// The caller owns disposal of the returned lease.</summary>
    FrameLease? AcquireCurrentFrame();

    /// <summary>Raised on the viewer STA after a frame commits. Does not report physical presentation.</summary>
    /// <remarks>A later acquisition may observe a different frame. Use FrameSubmissionOptions.OnCommitted
    /// when an operation needs the lease corresponding to a specific submission. Sender is the public
    /// Viewer or ImageViewerControl. Adding a subscriber rejects a stopping viewer with ObjectDisposedException;
    /// removing one is always safe. Duplicate subscriptions follow delegate semantics. Unsubscription does
    /// not retract captured notifications; subscriptions added during delivery apply only to later notifications.
    /// Synchronous subscriber failures are logged and isolated. Async-void work is not awaited;
    /// subscribers own its failures.</remarks>
    event EventHandler<FrameInfo>? FrameCommitted;
}
