namespace Fizzy.ImageViewer.Frames;

/// <summary>Reception outcome, independent of subsequent display. Inspect the result error for cleanup failures.</summary>
public enum FrameSubmitStatus
{
    /// <summary>The viewer accepted ownership into its latest-frame pipeline; display is not guaranteed.</summary>
    Accepted,
    /// <summary>Cancellation was observed before acceptance.</summary>
    Cancelled,
    /// <summary>The viewer was frozen and rejected the frame.</summary>
    Frozen,
    /// <summary>The viewer had stopped accepting frames.</summary>
    Closed
}
