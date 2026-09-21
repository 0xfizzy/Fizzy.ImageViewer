using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using OpenCvSharp;
using Robot.Core.Imaging;
using Robot.Core.Resources;
using Robot.Visualization.ImageViewer.Robot;
using Robot.Visualization.ImageViewer.OpenCvSharp;
using Xunit;
using FrameSource = Robot.Visualization.ImageViewer.Robot.FrameSource;

namespace RobotAdapter.Tests;

public class AdapterTests
{
    [Fact]
    public void OwnedRobotFrameSurvivesUntilLastLease()
    {
        var source = new TestFrame();
        var image = FrameSource.Take(source);
        var lease = image.Acquire(); image.Dispose();
        Assert.Equal(0, source.Releases);
        Assert.Equal(42, (lease.TryGetCpuPixels(out var data) ? data.Span[0] : throw new Exception("Expected CPU pixels")));
        lease.Dispose(); lease.Dispose(); Assert.Equal(1, source.Releases);
    }
    [Fact]
    public void UnsupportedRobotFormatIsReleasedExactlyOnce()
    {
        var source = new TestFrame { Format = (PixelFormat)999 };
        Assert.Throws<NotSupportedException>(() => FrameSource.Take(source));
        Assert.Equal(1, source.Releases);
    }
    [Fact]
    public void MatRoiIsCopiedBeforeCallerReusesStorage()
    {
        using var mat = new Mat(4, 5, MatType.CV_8UC1, Scalar.All(17));
        using var roi = new Mat(mat, new Rect(1, 1, 2, 2));
        using var image = MatSource.Copy(roi);
        mat.SetTo(Scalar.All(99));
        using var lease = image.Acquire();
        Assert.Equal(new byte[] { 17, 17, 17, 17 }, (lease.TryGetCpuPixels(out var pixels) ? pixels.ToArray() : throw new Exception("Expected CPU pixels")));
    }
    [Fact]
    public void FloatAndUnsigned16MatPreserveValues()
    {
        using var gray = new Mat(1, 1, MatType.CV_16UC1, Scalar.All(50000));
        using var floating = new Mat(1, 1, MatType.CV_32FC1, Scalar.All(-3.5));
        using var a = MatSource.Copy(gray); using var b = MatSource.Copy(floating);
        using var la = a.Acquire(); using var lb = b.Acquire();
        FramePixelReader.Instance.TryRead(la, 0, 0, out var pa);
        FramePixelReader.Instance.TryRead(lb, 0, 0, out var pb);
        Assert.Equal(50000, pa.Gray); Assert.Equal(-3.5, pb.Gray);
    }
    private sealed class TestFrame : IImageFrame
    {
        public int Width => 1;
        public int Height => 1;
        public PixelFormat Format { get; init; } = PixelFormat.Mono8;
        public ImageFrameMetadata Metadata => default;
        public Memory<byte> Buffer { get; } = new byte[] { 42 };
        public int Releases;
        public int RefCount => 1 - Releases;
        public void Retain() => throw new NotSupportedException();
        public void Release() => Releases++;
        public SharedResourceHandle Scope() => new(this);
    }
}
