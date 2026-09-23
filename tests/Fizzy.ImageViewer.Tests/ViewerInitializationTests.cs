using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ViewerInitializationTests
{
    [Fact]
    public async Task NormalShutdownWaitsForActualStaExit()
    {
        var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        var sta = await viewer.UiDispatcher.InvokeAsync(() => Thread.CurrentThread);
        await viewer.DisposeAsync();
        Assert.False(sta.IsAlive);
    }

    private sealed class Presenter(bool failDispose = false) : ICpuImagePresenter
    {
        public int Disposals;
        public ApartmentState Apartment;
        public ImageSource Present(DisplayBuffer pixels) => throw new NotSupportedException();
        public void Dispose()
        {
            Disposals++;
            Apartment = Thread.CurrentThread.GetApartmentState();
            if (failDispose) throw new InvalidOperationException("cleanup failure");
        }
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    [InlineData(double.PositiveInfinity, 10)]
    public void InvalidDimensionsFailBeforeStartingOrTakingPresenterOwnership(double width, double height)
    {
        var presenter = new Presenter();
        bool initialized = false;
        Assert.Throws<ArgumentOutOfRangeException>(() => new Viewer(NullLogger<Viewer>.Instance,
            presenter, false, width: width, height: height, initialize: _ => initialized = true));
        Assert.False(initialized);
        Assert.Equal(0, presenter.Disposals);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Viewer(NullLogger<Viewer>.Instance, left: double.NegativeInfinity));
        Assert.Throws<ArgumentNullException>(() => new Viewer(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupFailureUnwindsOwnersAndStopsStaBeforeRethrowing(bool cleanupFails)
    {
        var presenter = new Presenter(cleanupFails);
        var failure = new InvalidOperationException("startup failure");
        Thread? sta = null;
        Viewer? partial = null;
        int releasedScopes = 0, releasedFrames = 0, closedWindows = 0;
        var observed = Assert.Throws<InvalidOperationException>(() => new Viewer(NullLogger<Viewer>.Instance,
            presenter, false, initialize: viewer =>
            {
                partial = viewer;
                sta = Thread.CurrentThread;
                viewer.WindowForTests.Closed += (_, _) => closedWindows++;
                viewer.WindowForTests.Closing += (_, e) => e.Cancel = true;
                var scope = viewer.MeasurementContext.CreateScope();
                scope.AddShape(Shapes.CreatePoint(new()));
                scope.OnDispose(() => releasedScopes++);
                scope.Complete();
                _ = viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
                    new byte[] { 42 }, () => Interlocked.Increment(ref releasedFrames)));
                throw failure;
            }));
        Assert.Same(failure, observed);
        Assert.False(sta!.IsAlive);
        Assert.True(partial!.UiDispatcher.HasShutdownFinished);
        Assert.Equal(1, releasedScopes);
        Assert.Equal(1, releasedFrames);
        Assert.Equal(1, closedWindows);
        Assert.Equal(1, presenter.Disposals);
        Assert.Equal(ApartmentState.STA, presenter.Apartment);
        Assert.Throws<ObjectDisposedException>(() => partial.StartMeasure(MeasureToolIds.Point));
        await partial.DisposeAsync();
        Assert.Equal(1, presenter.Disposals);
    }
}
