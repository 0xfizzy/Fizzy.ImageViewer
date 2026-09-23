using System.Buffers.Binary;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

public sealed class FramePixelReader
{
    public static FramePixelReader Instance { get; } = new();
    public bool TryRead(FrameLease frame, int x, int y, out PixelSample sample)
    {
        var d = frame.Descriptor;
        sample = default;
        if ((uint)x >= (uint)d.Width || (uint)y >= (uint)d.Height) return false;
        var bytes = frame.CpuPixels.Span.Slice(y * d.Stride + x * d.Format.BytesPerPixel());
        sample = Decode(d.Format, bytes);
        return true;
    }
    public void ReadPixels(FrameLease frame, ReadOnlySpan<PixelCoordinate> coordinates, Span<PixelSample> samples)
    {
        if (samples.Length < coordinates.Length) throw new ArgumentException("Insufficient output capacity.");
        for (int i = 0; i < coordinates.Length; i++)
            if (!TryRead(frame, coordinates[i].X, coordinates[i].Y, out samples[i]))
                throw new ArgumentOutOfRangeException(nameof(coordinates));
    }
    internal static PixelSample Decode(FramePixelFormat format, ReadOnlySpan<byte> p) => format switch
    {
        FramePixelFormat.Gray8 => new(format, p[0], 0, 0, 0, 255),
        FramePixelFormat.Gray16 => new(format, BinaryPrimitives.ReadUInt16LittleEndian(p), 0, 0, 0, 255),
        FramePixelFormat.Gray32Float => new(format, BinaryPrimitives.ReadSingleLittleEndian(p), 0, 0, 0, 255),
        FramePixelFormat.Rgb24 => new(format, 0, p[0], p[1], p[2], 255),
        FramePixelFormat.Bgr24 or FramePixelFormat.Bgr32 => new(format, 0, p[2], p[1], p[0], 255),
        FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32 => new(format, 0, p[2], p[1], p[0], p[3]),
        _ => throw new NotSupportedException()
    };
}
