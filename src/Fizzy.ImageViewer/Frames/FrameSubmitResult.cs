namespace Fizzy.ImageViewer.Frames;

/// <summary>The reception outcome and any cleanup failure owned by this reception.</summary>
/// <param name="FrameId">Identity assigned to this submission.</param>
/// <param name="Status">Reception outcome; acceptance does not guarantee display.</param>
/// <param name="Error">Reception-owned cleanup failure, if any; subsequent display failures are reported separately.</param>
public readonly record struct FrameSubmitResult(long FrameId, FrameSubmitStatus Status, Exception? Error = null);
