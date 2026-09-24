namespace Fizzy.ImageViewer.Frames;

/// <summary>Access to committed original frames and commit notifications.</summary>
public interface ICommittedFrameSource
{
    /// <summary>Acquires an independent lease on the current committed frame, or null when none exists.
    /// The caller owns disposal of the returned lease.</summary>
    FrameLease? AcquireCurrentFrame();

    /// <summary>Raised on the viewer STA after a frame commits. Does not report physical presentation.</summary>
    /// <remarks>A later acquisition may observe a different frame. Use FrameSubmissionOptions.OnCommitted
    /// when an operation needs the lease corresponding to a specific submission.</remarks>
    event Action<FrameInfo>? FrameCommitted;
}
