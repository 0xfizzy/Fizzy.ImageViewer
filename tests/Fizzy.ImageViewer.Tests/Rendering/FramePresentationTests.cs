using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class FramePresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalUnlinksDisplayedBitmapEvenWhenPresenterCleanupFails(bool fail)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, null, false);
        var retained = await viewer.Host.Window.Dispatcher.InvokeAsync(() => CreateDisposedPresentation(viewer, fail));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(retained.Bitmap.IsAlive);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.Null(retained.Viewport.ImageDisplay.Source);
            retained.Presentation.Dispose();
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (FramePresentation Presentation, ImageViewport Viewport, WeakReference Bitmap) CreateDisposedPresentation(Viewer viewer, bool fail)
    {
        var viewport = new ImageViewport();
        var presenter = new CleanupPresenter(fail);
        var presentation = new FramePresentation(viewer.Host.Window.Dispatcher, viewport, presenter, NullLogger.Instance);
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(2, 2, 96, 96, System.Windows.Media.PixelFormats.Gray8, null);
        presentation.SetImage(bitmap, new(2, 2, 2, FramePixelFormat.Gray8));
        if (fail) Assert.Throws<InvalidOperationException>(presentation.Dispose);
        else presentation.Dispose();
        Assert.Equal(1, presenter.Disposals);
        presentation.Dispose();
        Assert.Equal(1, presenter.Disposals);
        return (presentation, viewport, new(bitmap));
    }

    private sealed class CleanupPresenter(bool fail) : ICpuImagePresenter
    {
        internal int Disposals;
        public System.Windows.Media.ImageSource Present(Fizzy.ImageViewer.Imaging.DisplayBuffer pixels) => throw new NotSupportedException();
        public void Dispose()
        {
            Disposals++;
            if (fail) throw new InvalidOperationException("presenter cleanup failed");
        }
    }

    [Fact]
    public async Task GpuPreparationDoesNotReadPixelsAndRejectsDisplayMapping()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, null, false);
        var presentation = await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
            new FramePresentation(viewer.Host.Window.Dispatcher, new ImageViewport(), new WriteableBitmapPresenter(), NullLogger.Instance));
        var source = new UnexpectedReadSource();
        int released = 0;
        using var frame = D3D9ImageFrame.TakeOwnership(new(1, 1, 4, FramePixelFormat.Bgra32),
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
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> points, CancellationToken cancellationToken) => throw Unexpected();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken cancellationToken) => throw Unexpected();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken cancellationToken) => throw Unexpected();
    }
}
