namespace Fizzy.ImageViewer.Frames;

public sealed record FrameSubmissionOptions
{
    public DateTimeOffset? SourceTimestamp { get; init; }
    /// <summary>Runs on the viewer STA after commit. Lease is borrowed only for this callback.</summary>
    public Action<FrameLease>? OnCommitted { get; init; }
}
