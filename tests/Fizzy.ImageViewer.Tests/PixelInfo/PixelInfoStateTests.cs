using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.PixelInfo;
using System.Globalization;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class PixelInfoStateTests
{
    [Theory]
    [InlineData(FramePixelFormat.Gray8)]
    [InlineData(FramePixelFormat.Gray16)]
    [InlineData(FramePixelFormat.Gray32Float)]
    [InlineData(FramePixelFormat.Pbgra32)]
    public void ColumnsRemainStableAcrossCoordinatesValuesAndCultures(FramePixelFormat format)
    {
        var descriptor = new FrameDescriptor(4096, 2048, 16384, format);
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var waiting = PixelInfoState.Format(descriptor, 9, 99, null);
            var small = PixelInfoState.Format(descriptor, 10, 100, new(format, 0.125, 0.125, 2, 3, 255));
            var extreme = PixelInfoState.Format(descriptor, 4095, 2047, new(format, -double.MaxValue, -double.MaxValue, double.NaN, double.PositiveInfinity, 255));
            Assert.Equal(waiting.Length, small.Length);
            Assert.Contains((-double.MaxValue).ToString("G7", CultureInfo.InvariantCulture), extreme);
            Assert.Contains("0.125", small);
            Assert.Equal(waiting.IndexOf('|'), extreme.IndexOf('|'));
            if (format == FramePixelFormat.Pbgra32)
            {
                Assert.EndsWith(" (premultiplied)", small);
                Assert.Equal(waiting.IndexOf("G:"), small.IndexOf("G:"));
            }
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void MovementRetainsWholePairAndInvalidCoordinatesHideImmediately()
    {
        var updates = new List<string?>();
        var hud = new PixelInfoState(updates.Add);
        hud.Enable(); hud.Move(9, 0);
        var descriptor = new FrameDescriptor(100, 1, 100, FramePixelFormat.Gray8);
        var request = Assert.IsType<PixelQueryRequest>(hud.Capture(descriptor));
        request.Publish(new FrameInfo(1, descriptor, null), [new(FramePixelFormat.Gray8, 42, 0, 0, 0, 255)]);
        var result = updates.Last();
        hud.Move(10, 0); hud.InvalidateResult(ResultInvalidation.CoordinatesChanged);
        Assert.Equal(result, updates.Last());
        hud.InvalidateResult(ResultInvalidation.Failed);
        Assert.StartsWith("X: 10,", updates.Last()); Assert.Contains("—", updates.Last());
        hud.Move(double.NaN, 0); Assert.Null(updates.Last());
        Assert.Null(hud.Capture(descriptor));
    }
}
