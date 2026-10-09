namespace Fizzy.ImageViewer.Frames;

/// <summary>Pixel description and viewer-local submission identity. FrameId zero denotes unsubmitted pixels.</summary>
/// <param name="FrameId">Viewer-local submission ID, or zero for standalone pixels.</param>
/// <param name="Descriptor">Layout of this frame's own pixels, including derived regions.</param>
/// <param name="SourceTimestamp">Producer-supplied capture timestamp, or null when unavailable.</param>
public readonly record struct FrameInfo(long FrameId, FrameDescriptor Descriptor, DateTimeOffset? SourceTimestamp);
