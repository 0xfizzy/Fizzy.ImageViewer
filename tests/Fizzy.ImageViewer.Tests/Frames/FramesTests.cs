using System.Buffers.Binary;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class FramesTests
{
    [Fact]
    public void LeasesHoldStorageAndReleaseExactlyOnce()
    {
        int releases = 0;
        var frame = ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }, () => releases++);
        var a = frame.Acquire(); var b = a.Acquire();
        frame.Dispose(); frame.Dispose(); a.Dispose(); a.Dispose();
        Assert.Equal(0, releases);
        Assert.Equal(42, b.CpuPixels.Span[0]);
        b.Dispose(); b.Dispose(); Assert.Equal(1, releases);
        Assert.Throws<ObjectDisposedException>(() => frame.Acquire());
    }
    [Fact]
    public void InvalidOwnedInputReleasesOnce()
    {
        int releases = 0;
        Assert.Throws<ArgumentException>(() => ImageFrame.TakeOwnership(new(2, 2, 2, FramePixelFormat.Gray16), new byte[4], () => releases++));
        Assert.Equal(1, releases);
    }
    [Fact]
    public void CopyAndPaddedStridePreservePixels()
    {
        byte[] bytes = [1, 2, 99, 99, 3, 4];
        using var frame = ImageFrame.Copy(new(2, 2, 4, FramePixelFormat.Gray8), bytes);
        Array.Fill(bytes, (byte)0);
        using var lease = frame.Acquire();
        Assert.True(FramePixelReader.Instance.TryRead(lease, 1, 1, out var value));
        Assert.Equal(4, value.Gray);
        Assert.False(FramePixelReader.Instance.TryRead(lease, -1, 0, out _));
    }
    [Theory]
    [InlineData(FramePixelFormat.Rgb24, 10, 20, 30, 10, 20, 30)]
    [InlineData(FramePixelFormat.Bgr24, 10, 20, 30, 30, 20, 10)]
    [InlineData(FramePixelFormat.Bgr32, 10, 20, 30, 30, 20, 10)]
    [InlineData(FramePixelFormat.Bgra32, 10, 20, 30, 30, 20, 10)]
    [InlineData(FramePixelFormat.Pbgra32, 10, 20, 30, 30, 20, 10)]
    public void ColorChannelsAreExplicit(FramePixelFormat format, byte a, byte b, byte c, double r, double g, double blue)
    {
        using var frame = ImageFrame.Copy(new(1, 1, format.BytesPerPixel(), format), new byte[] { a, b, c, 64 });
        using var lease = frame.Acquire();
        FramePixelReader.Instance.TryRead(lease, 0, 0, out var p);
        Assert.Equal(r, p.R); Assert.Equal(g, p.G); Assert.Equal(blue, p.B);
        Assert.Equal(format == FramePixelFormat.Pbgra32, p.IsPremultiplied);
    }
    [Fact]
    public void HighDepthSamplingAndDisplayAreIndependent()
    {
        byte[] data = new byte[4]; BinaryPrimitives.WriteUInt16LittleEndian(data, 12000); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 65535);
        using var frame = ImageFrame.Copy(new(2, 1, 4, FramePixelFormat.Gray16), data);
        using var lease = frame.Acquire();
        using var display = DisplayConverter.Convert(lease, new(0, 12000), default);
        Assert.Equal(255, display.Bytes[0]);
        FramePixelReader.Instance.TryRead(lease, 0, 0, out var p); Assert.Equal(12000, p.Gray);
    }
    [Fact]
    public async Task FloatSpecialValuesAndLineClipping()
    {
        byte[] data = new byte[16];
        float[] values = [float.NaN, float.NegativeInfinity, float.PositiveInfinity, .5f];
        for (int i = 0; i < 4; i++) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), values[i]);
        using var frame = ImageFrame.Copy(new(4, 1, 16, FramePixelFormat.Gray32Float), data);
        using var lease = frame.Acquire();
        using var display = DisplayConverter.Convert(lease, null, default);
        Assert.Equal(new byte[] { 0, 0, 255, 128 }, Enumerable.Range(0, 4).Select(i => display.Bytes[i * 4]).ToArray());
        var profile = new LineProfile(); var coordinates = profile.Prepare(lease.Descriptor, -1e9, 0, 1e9, 0);
        profile.Apply((await lease.ReadPixelsAsync(coordinates, default)).Samples);
        Assert.Equal(4, profile.Count); Assert.True(double.IsNaN(profile.Red[0]));
    }
}
