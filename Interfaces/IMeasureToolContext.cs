using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Interfaces;

public interface IMeasureToolContext
{
    Drawing.ShapeStyle Style { get; }
    FrameLease? AcquireCurrentFrame();
    IMeasurementScope CreateScope();
    event Action<FrameInfo>? FrameCommitted;
}
