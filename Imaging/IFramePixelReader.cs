using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

public readonly record struct PixelCoordinate(int X, int Y);
public interface IFramePixelReader
{
    bool TryRead(FrameLease frame, int x, int y, out PixelSample sample);
    void ReadPixels(FrameLease frame, ReadOnlySpan<PixelCoordinate> coordinates, Span<PixelSample> samples);
}
