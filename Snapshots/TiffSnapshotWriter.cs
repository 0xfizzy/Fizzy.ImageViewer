using System.IO;
using System.Text;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

/// <summary>Writes the single-page, uncompressed subset of Classic TIFF used by snapshots.</summary>
internal static class TiffSnapshotWriter
{
    internal sealed record Layout(int RowBytes, int Channels, int Bits, int RowsPerStrip,
        uint PixelOffset, uint FileSize, uint[] StripOffsets, uint[] StripByteCounts);

    internal static Layout CreateLayout(FrameDescriptor descriptor)
    {
        if (descriptor.Width <= 0 || descriptor.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(descriptor));
        // Also reject unknown formats before deriving the output layout.
        _ = descriptor.Format.BytesPerPixel();
        bool gray = IsGray(descriptor.Format), alpha = HasAlpha(descriptor.Format);
        int channels = gray ? 1 : alpha ? 4 : 3;
        int bits = descriptor.Format == FramePixelFormat.Gray16 ? 16 :
            descriptor.Format == FramePixelFormat.Gray32Float ? 32 : 8;
        long rowBytes = checked((long)descriptor.Width * channels * (bits / 8));
        long pixelBytes = checked(rowBytes * descriptor.Height);
        // Preserve the existing conservative Classic TIFF admission limit.
        if (checked(pixelBytes + (long)descriptor.Height * 16 + 65536) >= uint.MaxValue)
            throw new NotSupportedException("Image exceeds Classic TIFF size limit.");
        int rowSize = checked((int)rowBytes);
        int rowsPerStrip = Math.Max(1, 65536 / rowSize);
        int stripCount = checked((int)(((long)descriptor.Height + rowsPerStrip - 1) / rowsPerStrip));
        int tagCount = alpha ? 13 : 12;
        long pixelOffset = 8 + 2 + tagCount * 12 + 4;
        if (channels > 1) pixelOffset += channels * 2 * 2; // BitsPerSample and SampleFormat.
        if (stripCount > 1) pixelOffset += (long)stripCount * 4 * 2;
        var offsets = new uint[stripCount];
        var counts = new uint[stripCount];
        long end = pixelOffset;
        for (int strip = 0; strip < stripCount; strip++)
        {
            end = checked((end + 1) & ~1L);
            offsets[strip] = checked((uint)end);
            long rows = Math.Min(rowsPerStrip, descriptor.Height - (long)strip * rowsPerStrip);
            counts[strip] = checked((uint)(rows * rowBytes));
            end = checked(end + counts[strip]);
        }
        if (end >= uint.MaxValue)
            throw new NotSupportedException("Image exceeds Classic TIFF size limit.");
        return new(rowSize, channels, bits, rowsPerStrip, checked((uint)pixelOffset),
            checked((uint)end), offsets, counts);
    }

    internal static void Write(FrameLease frame, Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = frame.Descriptor;
        var layout = CreateLayout(descriptor);
        bool gray = IsGray(descriptor.Format), alpha = HasAlpha(descriptor.Format);
        // SHORT values of at most two elements fit in the IFD value field; LONG fits one.
        var tags = new List<Tag>
        {
            Long(256, (uint)descriptor.Width),
            Long(257, (uint)descriptor.Height),
            Short(258, Enumerable.Repeat((uint)layout.Bits, layout.Channels).ToArray()),
            Short(259, 1), // Compression: none.
            Short(262, gray ? 1u : 2u), // BlackIsZero or RGB.
            Long(273, layout.StripOffsets),
            Short(274, 1), // Top-left.
            Short(277, (uint)layout.Channels),
            Long(278, (uint)layout.RowsPerStrip),
            Long(279, layout.StripByteCounts),
            Short(284, 1), // Chunky (interleaved).
        };
        if (alpha) tags.Add(Short(338, descriptor.Format == FramePixelFormat.Pbgra32 ? 1u : 2u));
        tags.Add(Short(339, Enumerable.Repeat(descriptor.Format == FramePixelFormat.Gray32Float ? 3u : 1u,
            layout.Channels).ToArray()));

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0x4949); // II: little endian.
        writer.Write((ushort)42);
        writer.Write(8u);
        writer.Write((ushort)tags.Count);
        uint extraOffset = (uint)(8 + 2 + tags.Count * 12 + 4);
        foreach (var tag in tags)
        {
            writer.Write(tag.Id);
            writer.Write(tag.Type);
            writer.Write((uint)tag.Values.Length);
            if (tag.ByteCount <= 4)
            {
                WriteValues(writer, tag);
                for (int pad = tag.ByteCount; pad < 4; pad++) writer.Write((byte)0);
            }
            else
            {
                writer.Write(extraOffset);
                extraOffset = checked(extraOffset + (uint)tag.ByteCount);
            }
        }
        writer.Write(0u); // No following IFD.
        foreach (var tag in tags)
            if (tag.ByteCount > 4) WriteValues(writer, tag);

        byte[]? row = gray || descriptor.Format == FramePixelFormat.Rgb24 ? null : new byte[layout.RowBytes];
        long position = layout.PixelOffset;
        for (int y = 0; y < descriptor.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (y % layout.RowsPerStrip == 0 && (position & 1) != 0)
            {
                writer.Write((byte)0);
                position++;
            }
            var source = frame.CpuPixels.Span.Slice(y * descriptor.Stride, descriptor.RowBytes);
            if (row is null) writer.Write(source);
            else
            {
                int bytesPerPixel = descriptor.Format.BytesPerPixel();
                for (int x = 0; x < descriptor.Width; x++)
                {
                    row[x * layout.Channels] = source[x * bytesPerPixel + 2];
                    row[x * layout.Channels + 1] = source[x * bytesPerPixel + 1];
                    row[x * layout.Channels + 2] = source[x * bytesPerPixel];
                    if (alpha) row[x * layout.Channels + 3] = source[x * bytesPerPixel + 3];
                }
                writer.Write(row);
            }
            position += layout.RowBytes;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool IsGray(FramePixelFormat format)
        => format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float;
    private static bool HasAlpha(FramePixelFormat format)
        => format is FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32;
    private sealed record Tag(ushort Id, ushort Type, uint[] Values)
    {
        public int ByteCount => Values.Length * (Type == 3 ? 2 : 4);
    }
    private static Tag Short(ushort id, params uint[] values) => new(id, 3, values);
    private static Tag Long(ushort id, params uint[] values) => new(id, 4, values);
    private static void WriteValues(BinaryWriter writer, Tag tag)
    {
        foreach (uint value in tag.Values)
            if (tag.Type == 3) writer.Write(checked((ushort)value));
            else writer.Write(value);
    }
}
