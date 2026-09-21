namespace Fizzy.ImageViewer.Frames;

public readonly record struct FrameInfo(long FrameId, FrameDescriptor Descriptor, DateTimeOffset? SourceTimestamp);
