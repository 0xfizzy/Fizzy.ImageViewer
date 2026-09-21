namespace Fizzy.ImageViewer.Frames;

public enum FrameSubmitStatus { Committed, Superseded, Frozen, Cancelled, Closed, Failed }
public readonly record struct FrameSubmitResult(long FrameId, FrameSubmitStatus Status, Exception? Error = null);
