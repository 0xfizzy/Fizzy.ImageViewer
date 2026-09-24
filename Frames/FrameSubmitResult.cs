namespace Fizzy.ImageViewer.Frames;

public readonly record struct FrameSubmitResult(long FrameId, FrameSubmitStatus Status, Exception? Error = null);
