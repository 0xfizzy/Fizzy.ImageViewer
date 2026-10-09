namespace Fizzy.ImageViewer.Frames;

/// <summary>Optional source metadata and notification for one frame submission.</summary>
public sealed record FrameSubmissionOptions
{
    /// <summary>Producer-supplied capture timestamp, or null when unavailable; it does not control scheduling.</summary>
    public DateTimeOffset? SourceTimestamp { get; init; }
    /// <summary>Runs on the viewer STA after commit. Lease is borrowed only for this callback.</summary>
    public Action<FrameLease>? OnCommitted { get; init; }
}
