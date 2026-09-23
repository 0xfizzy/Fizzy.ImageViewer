using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Interfaces;

public interface IMeasureToolContext
{
    FrameLease? AcquireCurrentFrame();
    IMeasurementScope CreateScope();
    event Action<FrameInfo>? FrameCommitted;
}
