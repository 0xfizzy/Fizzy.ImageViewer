using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

public interface IMeasurementToolContext
{
    Drawing.ShapeStyle Style { get; }
    FrameLease? AcquireCurrentFrame();
    IMeasurementScope CreateScope();
    event Action<FrameInfo>? FrameCommitted;
}
