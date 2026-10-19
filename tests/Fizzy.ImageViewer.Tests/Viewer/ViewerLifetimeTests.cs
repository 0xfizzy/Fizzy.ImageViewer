using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ViewerLifetimeTests
{
    [Fact]
    public async Task RemovalRunsOnOwnerThreadAndBecomesANoOpAfterStopping()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var lifetime = new ViewerLifetime();
        var dispatcher = viewer.Host.Window.Dispatcher;
        int removed = 0;
        await Task.Run(() => lifetime.InvokeRemoval(dispatcher, () =>
        {
            dispatcher.VerifyAccess();
            removed++;
        }));
        lifetime.BeginDisposal();
        await Task.Run(() => lifetime.InvokeRemoval(dispatcher, () => removed++));
        await viewer.DisposeAsync();
        lifetime.InvokeRemoval(dispatcher, () => removed++);
        Assert.Equal(1, removed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovalDoesNotHideCallbackFailuresWhenCallbackStartsShutdown(bool cancelled)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var lifetime = new ViewerLifetime();
        Exception failure = cancelled ? new OperationCanceledException() : new InvalidOperationException();
        var actual = await Record.ExceptionAsync(() => Task.Run(() =>
            lifetime.InvokeRemoval(viewer.Host.Window.Dispatcher, () =>
            {
                lifetime.BeginDisposal();
                throw failure;
            })));
        // WPF translates a callback's cancellation into TaskCanceledException.
        if (cancelled) Assert.IsAssignableFrom<OperationCanceledException>(actual);
        else Assert.Same(failure, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StoppingIsObservableBeforeAcceptedWorkDrains(bool standalone) => ControlTestDispatcher.Run(async () =>
    {
        await using IViewer viewer = standalone ? new Viewer(showWindow: false) : new ImageViewerControl();
        IViewerLifetime observer = viewer;
        var stopping = observer.WhenStopping;
        Assert.Same(stopping, observer.WhenStopping);
        Assert.False(stopping.IsCompleted);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Task.Run(async () => await viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(
            new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }, () =>
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
            }), cancellationToken: new CancellationToken(true)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var closing = viewer.DisposeAsync().AsTask();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(closing.IsCompleted);
            int rejectedReleases = 0;
            var rejected = await viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(
                new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 1 }, () => rejectedReleases++));
            Assert.Equal(FrameSubmitStatus.Closed, rejected.Status);
            Assert.Equal(1, rejectedReleases);
            release.Set();
            await pending;
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(stopping, observer.WhenStopping);
            Assert.True(observer.WhenStopping.IsCompletedSuccessfully);
        }
        finally { release.Set(); await pending; }
    });

    [Fact]
    public async Task WindowClosureSignalsBorrowersWithoutDisposalRequest()
    {
        await using var viewer = new Viewer(showWindow: false);
        IViewerLifetime observer = viewer;
        await viewer.Host.Window.Dispatcher.InvokeAsync(viewer.Host.Window.CloseProgrammatically);
        await observer.WhenStopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observer.WhenStopping.IsCompletedSuccessfully);
    }
}
