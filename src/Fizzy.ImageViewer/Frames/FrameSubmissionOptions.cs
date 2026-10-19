namespace Fizzy.ImageViewer.Frames;

/// <summary>Optional source metadata and notification for one frame submission.</summary>
public sealed record FrameSubmissionOptions
{
    /// <summary>Producer-supplied capture timestamp, or null when unavailable; it does not control scheduling.</summary>
    public DateTimeOffset? SourceTimestamp { get; init; }
    /// <summary>Runs on the viewer STA after commit. Lease is borrowed only for this callback.</summary>
    /// <remarks>May run after reception returns, or never run if replaced, frozen or closed before display.
    /// Capture immutable metadata rather than dependencies disposed after reception.</remarks>
    public Action<FrameLease>? OnCommitted { get; init; }
}
