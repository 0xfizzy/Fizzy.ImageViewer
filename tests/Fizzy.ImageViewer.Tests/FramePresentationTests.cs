using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class FramePresentationTests
{
    [Fact]
    public async Task GpuPreparationDoesNotReadPixelsAndRejectsDisplayMapping()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, null, false);
        var presentation = await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
            new FramePresentation(viewer.Host.Window.Dispatcher, new ImageLayer(), new WriteableBitmapPresenter(), NullLogger.Instance));
        var source = new UnexpectedReadSource();
        int released = 0;
        using var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 4, FramePixelFormat.Bgra32),
            (nint)1, source, () => released++);
        using var lease = frame.Acquire();
        try
        {
            await Task.Run(() =>
            {
                // Preparation must never bind this dummy surface or query its pixel source.
                using var prepared = presentation.Prepare(lease, null, default);
                Assert.Null(prepared.Pixels);
                Assert.Same(lease, prepared.Frame);
                Assert.Throws<NotSupportedException>(() => presentation.Prepare(lease, new(0, 255), default));
                Assert.Throws<OperationCanceledException>(() => presentation.Prepare(lease, null, new CancellationToken(true)));
            });
            Assert.Equal(0, source.Reads);
        }
        finally { await viewer.Host.Window.Dispatcher.InvokeAsync(presentation.Dispose); }
        frame.Dispose();
        Assert.Equal(0, released);
        lease.Dispose();
        Assert.Equal(1, released);
    }

    private sealed class UnexpectedReadSource : IFramePixelSource
    {
        internal int Reads;
        private Exception Unexpected() { Reads++; return new InvalidOperationException("Unexpected pixel read during presentation"); }
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> points, CancellationToken ct) => throw Unexpected();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct) => throw Unexpected();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct) => throw Unexpected();
    }
}
