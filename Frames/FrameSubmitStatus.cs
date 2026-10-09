namespace Fizzy.ImageViewer.Frames;

/// <summary>Terminal submission outcome. Inspect FrameSubmitResult.Error separately for processing or cleanup failures.</summary>
public enum FrameSubmitStatus
{
    /// <summary>The frame became current on the viewer STA; this does not confirm physical display.</summary>
    Committed,
    /// <summary>A newer submission replaced this waiting frame.</summary>
    Superseded,
    /// <summary>The frame was dropped because the viewer was frozen or its freeze epoch changed.</summary>
    Frozen,
    /// <summary>Cancellation was observed before commit.</summary>
    Cancelled,
    /// <summary>The viewer stopped accepting or processing this submission.</summary>
    Closed,
    /// <summary>Frame processing failed; the result carries the error.</summary>
    Failed
}
