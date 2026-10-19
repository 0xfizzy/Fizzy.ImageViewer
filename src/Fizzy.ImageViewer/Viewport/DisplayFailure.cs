using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Viewport;

/// <summary>A failed display attempt, independent of frame acceptance.</summary>
/// <param name="Frame">The frame whose presentation failed.</param>
/// <param name="DisplayRange">The requested mapping captured for this attempt.</param>
/// <param name="DisplayVersion">The mapping version captured for this attempt.</param>
/// <param name="Error">The preparation or presentation failure.</param>
public sealed record DisplayFailure(FrameInfo Frame, GrayDisplayRange? DisplayRange, long DisplayVersion, Exception Error);
