using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ViewerInitializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalLoggerSupportsFramesAndShutdown(bool explicitNull)
    {
        await using var viewer = explicitNull ? new Viewer(logger: null, showWindow: false) : new Viewer(showWindow: false);
        Assert.False(viewer.IsVisible);
        var sta = await viewer.Host.Window.Dispatcher.InvokeAsync(() => Thread.CurrentThread);
        Assert.Equal(FrameSubmitStatus.Committed, (await viewer.SubmitFrameAsync(
            ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }))).Status);
        await viewer.DisposeAsync();
        Assert.False(sta.IsAlive);
    }

    [Fact]
    public async Task InjectedLoggerReceivesIsolatedSubscriberFailure()
    {
        var logger = new RecordingLogger();
        await using var viewer = new Viewer(logger, showWindow: false);
        var failure = new InvalidOperationException("subscriber failure");
        viewer.Closed += (_, _) => throw failure;
        await viewer.DisposeAsync();
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning &&
            ReferenceEquals(entry.Exception, failure) && entry.Message == "Closed handler failed");
    }

    private sealed class RecordingLogger : ILogger<Viewer>
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, Exception? Exception, string Message)> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Enqueue((logLevel, exception, formatter(state, exception)));
    }

    [Fact]
    public async Task StartupRollbackPreservesFailureAndStopsSta()
    {
        var presenter = new Presenter();
        var failure = new InvalidOperationException("startup failure");
        Thread? sta = null;
        var observed = await Task.Run(() => Assert.Throws<InvalidOperationException>(() =>
            new Viewer(NullLogger<Viewer>.Instance, presenter, false,
                initialize: _ => { sta = Thread.CurrentThread; throw failure; })))
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(failure, observed);
        Assert.False(sta!.IsAlive);
        Assert.Equal(1, presenter.Disposals);
    }

    [Fact]
    public async Task WindowClosureAndExplicitDisposalShareCleanup()
    {
        var presenter = new Presenter();
        var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, false);
        var sta = await viewer.Host.Window.Dispatcher.InvokeAsync(() => Thread.CurrentThread);
        await viewer.Host.Window.Dispatcher.InvokeAsync(viewer.Host.Window.CloseProgrammatically);
        await viewer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(sta.IsAlive);
        Assert.Equal(1, presenter.Disposals);
    }

    [Fact]
    public async Task NormalShutdownWaitsForActualStaExit()
    {
        var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        var sta = await viewer.Host.Window.Dispatcher.InvokeAsync(() => Thread.CurrentThread);
        await viewer.DisposeAsync();
        Assert.False(sta.IsAlive);
    }

    [Fact]
    public async Task PublicHiddenCreationAllowsSetupBeforeShowing()
    {
        await using var viewer = new Viewer(showWindow: false);
        Assert.False(viewer.IsVisible);
        viewer.Title = "Configured before show";
        var closed = 0;
        viewer.Closed += (_, _) => closed++;
        Assert.Equal(FrameSubmitStatus.Committed, (await viewer.SubmitFrameAsync(
            ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }))).Status);
        Assert.False(viewer.IsVisible);
        viewer.Show();
        Assert.True(viewer.IsVisible);
        Assert.Equal("Configured before show", viewer.Title);
        await viewer.DisposeAsync();
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task NeverShownViewerClosesAndStopsSta()
    {
        var viewer = new Viewer(showWindow: false);
        var sta = await viewer.Host.Window.Dispatcher.InvokeAsync(() => Thread.CurrentThread);
        var closed = 0;
        viewer.Closed += (_, _) => closed++;
        await viewer.DisposeAsync();
        await viewer.DisposeAsync();
        Assert.False(sta.IsAlive);
        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task PartialCompositionFailurePreservesExceptionAndReleasesPresenterOnSta(int stage)
    {
        var presenter = new Presenter(failDispose: true);
        var failure = new InvalidOperationException("partial composition failure");
        Thread? sta = null;
        var observed = await Task.Run(() => Assert.Throws<InvalidOperationException>(() =>
            new Viewer(NullLogger<Viewer>.Instance, presenter, false, checkpoint: current =>
            {
                if ((int)current != stage) return;
                sta = Thread.CurrentThread;
                throw failure;
            }))).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(failure, observed);
        Assert.False(sta!.IsAlive);
        Assert.Equal(1, presenter.Disposals);
        Assert.Equal(ApartmentState.STA, presenter.Apartment);
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
                viewer.Host.Window.Closed += (_, _) => closedWindows++;
                viewer.Host.Window.Closing += (_, e) => e.Cancel = true;
                var scope = new MeasurementCreationContext(viewer.Host.Measurements).CreateMeasurement(MeasurementGeometry.Point(new()));

                scope.OnDispose(() => releasedScopes++);
                scope.Complete();
                _ = viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
                    new byte[] { 42 }, () => Interlocked.Increment(ref releasedFrames)));
                throw failure;
            }));
        Assert.Same(failure, observed);
        Assert.False(sta!.IsAlive);
        Assert.True(partial!.Host.Window.Dispatcher.HasShutdownFinished);
        Assert.Equal(1, releasedScopes);
        Assert.Equal(1, releasedFrames);
        Assert.Equal(1, closedWindows);
        Assert.Equal(1, presenter.Disposals);
        Assert.Equal(ApartmentState.STA, presenter.Apartment);
        Assert.Throws<ObjectDisposedException>(() => partial.StartMeasurement(MeasurementToolIds.Point));
        await partial.DisposeAsync();
        Assert.Equal(1, presenter.Disposals);
    }
}
