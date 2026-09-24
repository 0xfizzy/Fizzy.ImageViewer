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
}
