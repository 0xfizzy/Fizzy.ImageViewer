using Fizzy.ImageViewer.Menus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using Xunit;
using ActionItem = Fizzy.ImageViewer.Menus.MenuItem;
using WpfItem = System.Windows.Controls.MenuItem;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MenuExecutionTests
{
    private sealed class ErrorLog : ILogger
    {
        internal TaskCompletionSource<Exception> Error = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (exception != null) Error.TrySetResult(exception); }
    }

    [Fact]
    public async Task AsyncActionIsAwaitedAcrossReopeningAndItsFailureIsObservedAfterClosure()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        var log = new ErrorLog();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("async menu failure");
        MenuManager? manager = null;
        IDisposable? registration = null;
        int calls = 0, otherCalls = 0;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var target = new Border();
            manager = new MenuManager(target, logger: log);
            registration = manager.Register(new ActionItem("async", () =>
            {
                viewer.Host.Window.Dispatcher.VerifyAccess();
                calls++;
                return FailAfterAsync(release.Task, failure);
            }));
            manager.Register(new ActionItem("other", () => otherCalls++));
            var menu = target.ContextMenu;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            var first = menu.Items.OfType<WpfItem>().Single(i => Equals(i.Header, "async"));
            first.RaiseEvent(new RoutedEventArgs(WpfItem.ClickEvent));
            first.RaiseEvent(new RoutedEventArgs(WpfItem.ClickEvent));
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            var reopened = menu.Items.OfType<WpfItem>().Single(i => Equals(i.Header, "async"));
            reopened.RaiseEvent(new RoutedEventArgs(WpfItem.ClickEvent));
            menu.Items.OfType<WpfItem>().Single(i => Equals(i.Header, "other"))
                .RaiseEvent(new RoutedEventArgs(WpfItem.ClickEvent));
            registration.Dispose();
            reopened.RaiseEvent(new RoutedEventArgs(WpfItem.ClickEvent));
            manager.Dispose();
        });
        Assert.Equal(1, calls);
        Assert.Equal(1, otherCalls);
        await viewer.DisposeAsync();
        release.SetResult();
        Assert.Same(failure, await log.Error.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static async ValueTask FailAfterAsync(Task release, Exception failure)
    {
        // The action deliberately outlives the viewer dispatcher.
        await release.ConfigureAwait(false);
        throw failure;
    }

    [Fact]
    public async Task DirectMenuExecutionReturnsTheActionsCompletionAndFailure()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException();
        IMenuItem item = new ActionItem("async", async () => { await release.Task; throw failure; });
        var pending = item.ExecuteAsync().AsTask();
        Assert.False(pending.IsCompleted);
        release.SetResult();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => pending));
    }
}
