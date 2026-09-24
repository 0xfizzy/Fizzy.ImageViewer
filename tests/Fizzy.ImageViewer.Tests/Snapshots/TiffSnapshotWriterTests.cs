using System.IO;
using BitMiracle.LibTiff.Classic;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Snapshots;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class TiffSnapshotWriterTests
{
    public static IEnumerable<object[]> FormatsAndSizes()
    {
        foreach (var format in Enum.GetValues<FramePixelFormat>())
            foreach (var (width, height) in new[] { (1, 1), (3, 2), (257, 263), (65537, 3) })
                yield return new object[] { format, width, height };
    }

    [Theory]
    [MemberData(nameof(FormatsAndSizes))]
    public async Task IndependentDecoderReadsAllFormatsAndStripLayouts(FramePixelFormat format, int width, int height)
    {
        int bytesPerPixel = format.BytesPerPixel(), stride = width * bytesPerPixel + 5;
        byte[] pixels = new byte[stride * height];
        new Random(42).NextBytes(pixels);
        if (format == FramePixelFormat.Gray32Float)
        {
            uint[] special = [0x7fc00042, 0x7f800000, 0xff800000, 0x80000000];
            for (int i = 0; i < Math.Min(width, special.Length); i++)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(pixels.AsSpan(i * 4), special[i]);
        }
        var descriptor = new FrameDescriptor(width, height, stride, format);
        using var snapshot = new ImageSnapshot(ImageFrame.Copy(descriptor, pixels), default, SnapshotKind.Raw, 0, default);
        string path = Path.Combine(Path.GetTempPath(), $"viewer-{Guid.NewGuid():N}.tif");
        try
        {
            await snapshot.SaveAsync(path, SnapshotEncoding.Tiff);
            // Disable reader strip chopping so assertions observe the on-disk layout.
            using var tiff = Tiff.Open(path, "rc");
            Assert.NotNull(tiff);
            bool gray = format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float;
            bool alpha = format is FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32;
            int channels = gray ? 1 : alpha ? 4 : 3;
            int bits = format == FramePixelFormat.Gray16 ? 16 : format == FramePixelFormat.Gray32Float ? 32 : 8;
            Assert.Equal(width, tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt());
            Assert.Equal(height, tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt());
            Assert.Equal(bits, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt());
            Assert.Equal(channels, tiff.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt());
            Assert.Equal(format == FramePixelFormat.Gray32Float ? 3 : 1, tiff.GetField(TiffTag.SAMPLEFORMAT)[0].ToInt());
            Assert.Equal(gray ? 1 : 2, tiff.GetField(TiffTag.PHOTOMETRIC)[0].ToInt());
            Assert.Equal(1, tiff.GetField(TiffTag.COMPRESSION)[0].ToInt());
            Assert.Equal(1, tiff.GetField(TiffTag.ORIENTATION)[0].ToInt());
            Assert.Equal(1, tiff.GetField(TiffTag.PLANARCONFIG)[0].ToInt());
            if (alpha)
            {
                Assert.Equal(1, tiff.GetField(TiffTag.EXTRASAMPLES)[0].ToInt());
                Assert.Equal((short)(format == FramePixelFormat.Pbgra32 ? 1 : 2),
                    tiff.GetField(TiffTag.EXTRASAMPLES)[1].ToShortArray()[0]);
            }
            else Assert.Null(tiff.GetField(TiffTag.EXTRASAMPLES));
            int rowBytes = width * channels * bits / 8, rowsPerStrip = Math.Max(1, 65536 / rowBytes);
            Assert.Equal(rowsPerStrip, tiff.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt());
            Assert.Equal((height + rowsPerStrip - 1) / rowsPerStrip, tiff.NumberOfStrips());
            byte[] actual = new byte[rowBytes], expected = new byte[rowBytes];
            for (int y = 0; y < height; y++)
            {
                Assert.True(tiff.ReadScanline(actual, y));
                if (gray || format == FramePixelFormat.Rgb24)
                    Array.Copy(pixels, y * stride, expected, 0, rowBytes);
                else
                    for (int x = 0; x < width; x++)
                    {
                        int source = y * stride + x * bytesPerPixel, target = x * channels;
                        expected[target] = pixels[source + 2];
                        expected[target + 1] = pixels[source + 1];
                        expected[target + 2] = pixels[source];
                        if (alpha) expected[target + 3] = pixels[source + 3];
                    }
                Assert.Equal(expected, actual);
            }
            Assert.False(tiff.ReadDirectory());
            Assert.Equal((long)TiffSnapshotWriter.CreateLayout(descriptor).FileSize, new FileInfo(path).Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LayoutRejectsOversizeWithoutAllocatingPixels()
    {
        const int width = 65536;
        int maximumHeight = (int)((uint.MaxValue - 1L - 65536) / (width + 16L));
        var accepted = TiffSnapshotWriter.CreateLayout(new(width, maximumHeight, width, FramePixelFormat.Gray8));
        Assert.True(accepted.FileSize < uint.MaxValue);
        Assert.Throws<NotSupportedException>(() => TiffSnapshotWriter.CreateLayout(
            new(width, maximumHeight + 1, width, FramePixelFormat.Gray8)));
        Assert.Throws<OverflowException>(() => TiffSnapshotWriter.CreateLayout(
            new(int.MaxValue, int.MaxValue, int.MaxValue, FramePixelFormat.Gray32Float)));
        Assert.Throws<OverflowException>(() => TiffSnapshotWriter.CreateLayout(
            new(800_000_000, 1, int.MaxValue, FramePixelFormat.Rgb24)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopsBetweenRowsAndLeavesCallerStreamOpen(bool cancel)
    {
        using var snapshot = new ImageSnapshot(ImageFrame.Copy(new(3, 3, 3, FramePixelFormat.Gray8), new byte[9]),
            default, SnapshotKind.Raw, 0, default);
        using var frame = snapshot.AcquirePixels();
        var layout = TiffSnapshotWriter.CreateLayout(frame.Descriptor);
        using var cts = new CancellationTokenSource();
        using var stream = new ControlledStream(layout.PixelOffset + (uint)layout.RowBytes, cancel ? cts : null);
        if (cancel) Assert.Throws<OperationCanceledException>(() => TiffSnapshotWriter.Write(frame, stream, cts.Token));
        else Assert.Throws<IOException>(() => TiffSnapshotWriter.Write(frame, stream, default));
        Assert.True(stream.CanWrite);
        Assert.Equal((long)layout.PixelOffset + layout.RowBytes, stream.Length);
    }

    [Fact]
    public async Task FailedReplacementAndCancellationPreserveTargetAndCleanTemporaryFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "snapshot.tif");
        using var snapshot = new ImageSnapshot(ImageFrame.Copy(new(1, 1, 4, FramePixelFormat.Pbgra32),
            new byte[] { 10, 20, 30, 64 }), default, SnapshotKind.Display, 0, default);
        try
        {
            await File.WriteAllTextAsync(path, "original");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failure = await Record.ExceptionAsync(() => snapshot.SaveAsync(path, SnapshotEncoding.Tiff));
                Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString() ?? "Save unexpectedly succeeded.");
            }
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(directory));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => snapshot.SaveAsync(path,
                SnapshotEncoding.Tiff, new CancellationToken(true)));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(directory));
            await snapshot.SaveAsync(path, SnapshotEncoding.Tiff);
            // Disable reader strip chopping so assertions observe the on-disk layout.
            using var tiff = Tiff.Open(path, "rc");
            byte[] row = new byte[4];
            Assert.True(tiff.ReadScanline(row, 0));
            Assert.Equal(new byte[] { 30, 20, 10, 64 }, row);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class ControlledStream(long stopAfter, CancellationTokenSource? cancellation) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            BeforeWrite();
            base.Write(buffer, offset, count);
            AfterWrite();
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BeforeWrite();
            base.Write(buffer);
            AfterWrite();
        }
        private void BeforeWrite()
        {
            if (Length >= stopAfter && cancellation is null) throw new IOException("Injected write failure.");
        }
        private void AfterWrite()
        {
            if (Length >= stopAfter) cancellation?.Cancel();
        }
    }
}
