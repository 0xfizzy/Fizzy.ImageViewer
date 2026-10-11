using System.Buffers;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

internal sealed class DisplayBuffer : IDisposable
{
    public byte[] Bytes { get; }
    public int Width { get; }
    public int Height { get; }
    public FramePixelFormat Format { get; }
    public int Stride => checked(Width * Format.BytesPerPixel());
    public int Length => checked(Stride * Height);
    public DisplayBuffer(int width, int height, FramePixelFormat format = FramePixelFormat.Pbgra32)
    {
        Width = width; Height = height; Format = format;
        Bytes = ArrayPool<byte>.Shared.Rent(Length);
    }
    public void Dispose() => ArrayPool<byte>.Shared.Return(Bytes);
}
