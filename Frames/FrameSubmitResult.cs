namespace Fizzy.ImageViewer.Frames;

/// <summary>The actual commit/drop outcome and any processing or submission-owned cleanup failure.</summary>
/// <param name="FrameId">Identity assigned to this submission.</param>
/// <param name="Status">Actual commit/drop outcome; a cleanup failure does not undo a commit.</param>
/// <param name="Error">Processing or cleanup failure, if any. Inspect this even when Status is not Failed.</param>
public readonly record struct FrameSubmitResult(long FrameId, FrameSubmitStatus Status, Exception? Error = null);
