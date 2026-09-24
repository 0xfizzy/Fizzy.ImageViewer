namespace Fizzy.ImageViewer.Frames;

/// <summary>Pixel description and viewer-local submission identity. FrameId zero denotes unsubmitted pixels.</summary>
public readonly record struct FrameInfo(long FrameId, FrameDescriptor Descriptor, DateTimeOffset? SourceTimestamp);
