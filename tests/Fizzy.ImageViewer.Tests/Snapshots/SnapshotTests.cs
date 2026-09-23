using System.IO;
using BitMiracle.LibTiff.Classic;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Snapshots;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class SnapshotTests
{
    [Theory]
    [InlineData(FramePixelFormat.Gray16)]
    [InlineData(FramePixelFormat.Gray32Float)]
    public async Task TiffRoundtripPreservesRawBitsAndOmitsPadding(FramePixelFormat format)
    {
        int rowBytes = 3 * format.BytesPerPixel(), stride = rowBytes + 4;
        byte[] bytes = new byte[stride * 2];
        new Random(42).NextBytes(bytes);
        // Include a NaN payload and infinity without normalizing either.
        if (format == FramePixelFormat.Gray32Float) { bytes[0] = 0x42; bytes[1] = 0; bytes[2] = 0xc0; bytes[3] = 0x7f; }
        using var snapshot = new ImageSnapshot(ImageFrame.Copy(new(3, 2, stride, format), bytes), default, SnapshotKind.Raw, 0, default);
        string path = Path.Combine(Path.GetTempPath(), $"viewer-{Guid.NewGuid():N}.tif");
        try
        {
            await snapshot.SaveAsync(path, SnapshotEncoding.Tiff);
            using var tiff = Tiff.Open(path, "r");
            Assert.Equal(format == FramePixelFormat.Gray16 ? 16 : 32, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt());
            Assert.Equal(format == FramePixelFormat.Gray16 ? (int)SampleFormat.UINT : (int)SampleFormat.IEEEFP, tiff.GetField(TiffTag.SAMPLEFORMAT)[0].ToInt());
            byte[] row = new byte[rowBytes];
            for (int y = 0; y < 2; y++)
            {
                Assert.True(tiff.ReadScanline(row, y));
                Assert.Equal(bytes.AsSpan(y * stride, rowBytes).ToArray(), row);
            }
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task ColorTiffUsesRgbOrderAndAssociatedAlpha()
    {
        using var snapshot = new ImageSnapshot(ImageFrame.Copy(new(1, 1, 4, FramePixelFormat.Pbgra32), new byte[] { 10, 20, 30, 64 }), default, SnapshotKind.Raw, 0, default);
        string path = Path.Combine(Path.GetTempPath(), $"viewer-{Guid.NewGuid():N}.tif");
        try
        {
            await snapshot.SaveAsync(path, SnapshotEncoding.Tiff);
            using var tiff = Tiff.Open(path, "r");
            byte[] row = new byte[4];
            Assert.True(tiff.ReadScanline(row, 0));
            Assert.Equal(new byte[] { 30, 20, 10, 64 }, row);
            Assert.Equal((int)Photometric.RGB, tiff.GetField(TiffTag.PHOTOMETRIC)[0].ToInt());
            Assert.Equal((short)ExtraSample.ASSOCALPHA, tiff.GetField(TiffTag.EXTRASAMPLES)[1].ToShortArray()[0]);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task CancelledSavePreservesExistingFile()
    {
        using var snapshot = new ImageSnapshot(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 1 }), default, SnapshotKind.Raw, 0, default);
        string path = Path.Combine(Path.GetTempPath(), $"viewer-{Guid.NewGuid():N}.tif");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => snapshot.SaveAsync(path, SnapshotEncoding.Tiff, new CancellationToken(true)));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            await Assert.ThrowsAsync<ArgumentException>(() => snapshot.SaveAsync(path, SnapshotEncoding.Jpeg));
        }
        finally { File.Delete(path); }
    }
}
