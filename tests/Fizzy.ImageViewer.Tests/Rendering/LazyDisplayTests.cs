using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class LazyDisplayTests
{
    [Fact]
    public async Task HiddenReceptionIsBoundedAndResumesLatestWithoutNewInput()
    {
        var presenter = new CountingPresenter();
        var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, false);
        int released = 0;
        var shown = new TaskCompletionSource<FrameInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewer.FrameCommitted += (_, info) => shown.TrySetResult(info);
        FrameSubmitResult latest = default;
        for (int i = 0; i < 100; i++)
        {
            latest = await viewer.SubmitFrameAsync(Frame((byte)i, () => Interlocked.Increment(ref released)));
            Assert.Equal(FrameSubmitStatus.Accepted, latest.Status);
        }
        Assert.Equal(99, released);
        Assert.Equal(0, presenter.Calls);
        Assert.True(viewer.Host.Control.Runtime.Pipeline.Completion.IsCompleted);
        Assert.Null(viewer.AcquireCurrentFrame());
        viewer.Visible = true;
        Assert.Equal(latest.FrameId, (await shown.Task.WaitAsync(TimeSpan.FromSeconds(5))).FrameId);
        await viewer.Host.Control.Runtime.Pipeline.Completion;
        using var lease = viewer.AcquireCurrentFrame();
        Assert.Equal(99, lease!.CpuPixels.Span[0]);
        viewer.Hide(); viewer.Visible = true;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, presenter.Calls);
        await viewer.DisposeAsync();
        Assert.Equal(99, released);
        Assert.Equal(99, lease.CpuPixels.Span[0]);
        lease.Dispose();
        Assert.Equal(100, released);
    }

    [Fact]
    public async Task MinimizedVisibleWindowReceivesButDoesNotPresent()
    {
        var presenter = new CountingPresenter();
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, true);
        await viewer.SubmitAndDisplayAsync(Frame(1));
        viewer.Minimize();
        Assert.True(viewer.IsVisible);
        Assert.True(viewer.IsMinimized);
        viewer.Visible = true;
        Assert.True(viewer.IsMinimized);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await viewer.SubmitFrameAsync(Frame(2), new() { OnCommitted = _ => shown.TrySetResult() });
        Assert.Equal(1, presenter.Calls);
        using (var previous = viewer.AcquireCurrentFrame()) Assert.Equal(1, previous!.CpuPixels.Span[0]);
        viewer.Show();
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(viewer.IsMinimized);
        Assert.Equal(2, presenter.Calls);
    }

    [Fact]
    public Task AncestorVisibilityUnloadAndHostReplacementControlDisplay() => ControlTestDispatcher.Run(async () =>
    {
        var presenter = new CountingPresenter();
        await using var control = new ImageViewerControl(NullLogger.Instance, presenter, null);
        var parent = new Grid(); parent.Children.Add(control);
        var first = new Window { Content = parent, ShowActivated = false, Width = 200, Height = 200 };
        var second = new Window { ShowActivated = false, Width = 200, Height = 200 };
        try
        {
            first.Show();
            await control.SubmitAndDisplayAsync(Frame(1));
            parent.Visibility = Visibility.Collapsed;
            Assert.True(control.Visible);
            Assert.False(control.IsVisible);
            await control.SubmitFrameAsync(Frame(2));
            Assert.Equal(1, presenter.Calls);
            parent.Visibility = Visibility.Visible;
            await control.Runtime.Pipeline.Completion;
            await control.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(2, presenter.Calls);
            parent.Children.Remove(control);
            await control.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await control.SubmitFrameAsync(Frame(3));
            Assert.Equal(2, presenter.Calls);
            Assert.False(control.IsMinimized);
            second.Content = control; second.Show();
            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control.FrameCommitted += (_, _) => shown.TrySetResult();
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            first.WindowState = WindowState.Minimized;
            Assert.False(control.IsMinimized);
            control.Visible = false;
            Assert.Equal(Visibility.Hidden, control.Visibility);
            await control.SubmitFrameAsync(Frame(4));
            Assert.Equal(3, presenter.Calls);
            control.Visible = true;
            await control.Runtime.Pipeline.Completion;
            Assert.Equal(4, presenter.Calls);
        }
        finally { await control.DisposeAsync(); first.Close(); second.Close(); }
    });

    [Fact]
    public Task TabHostVisibilityBindingResumesAcceptedFrameAfterCancellation() => ControlTestDispatcher.Run(async () =>
    {
        var presenter = new CountingPresenter();
        await using var control = new ImageViewerControl(NullLogger.Instance, presenter, null);
        var selected = new TabItem { Header = "Viewer" };
        var other = new TabItem { Header = "Other" };
        var tabs = new TabControl { Items = { selected, other }, SelectedItem = other };
        control.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(TabItem.IsSelected))
        {
            Source = selected,
            Converter = new BooleanToVisibilityConverter()
        });
        var layout = new StackPanel();
        layout.Children.Add(tabs); layout.Children.Add(control);
        var window = new Window { Content = layout, ShowActivated = false, Width = 200, Height = 200 };
        try
        {
            window.Show();
            using var cancellation = new CancellationTokenSource();
            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = await control.SubmitFrameAsync(Frame(7), new() { OnCommitted = _ => shown.TrySetResult() }, cancellation.Token);
            Assert.Equal(FrameSubmitStatus.Accepted, result.Status);
            cancellation.Cancel();
            Assert.False(control.IsVisible);
            Assert.Equal(0, presenter.Calls);
            tabs.SelectedItem = selected;
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(control.IsVisible);
            Assert.Equal(1, presenter.Calls);
            using var current = control.AcquireCurrentFrame();
            Assert.Equal(7, current!.CpuPixels.Span[0]);
        }
        finally { await control.DisposeAsync(); window.Close(); }
    });

    [Fact]
    public async Task QueuedCommitDefersWhenHiddenAndResumesAfterUnhide()
    {
        await using var viewer = new Viewer(showWindow: true);
        await viewer.SubmitAndDisplayAsync(Frame(1));
        var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queued = new ManualResetEventSlim();
        var dispatcher = viewer.Host.Window.Dispatcher;
        System.Windows.Threading.DispatcherHookEventHandler hook = (_, e) =>
        {
            if (e.Operation.Priority == DispatcherPriority.Render) queued.Set();
        };
        dispatcher.Hooks.OperationPosted += hook;
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                _ = viewer.SubmitFrameAsync(Frame(2), new() { OnCommitted = _ => committed.TrySetResult() });
                Assert.True(queued.Wait(TimeSpan.FromSeconds(5)));
                viewer.Hide();
                submitted.TrySetResult();
            });
            await submitted.Task;
            await viewer.Host.Control.Runtime.Pipeline.Completion;
            Assert.False(committed.Task.IsCompleted);
            using (var previous = viewer.AcquireCurrentFrame()) Assert.Equal(1, previous!.CpuPixels.Span[0]);
            viewer.Visible = true;
            await committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var current = viewer.AcquireCurrentFrame();
            Assert.Equal(2, current!.CpuPixels.Span[0]);
        }
        finally { dispatcher.Hooks.OperationPosted -= hook; }
    }

    [Fact]
    public async Task ReplacementReleaseFailureBelongsToCloseNotNewReceipt()
    {
        var viewer = new Viewer(showWindow: false);
        var error = new InvalidOperationException("old storage");
        await viewer.SubmitFrameAsync(Frame(1, () => throw error));
        var next = await viewer.SubmitFrameAsync(Frame(2));
        Assert.Equal(FrameSubmitStatus.Accepted, next.Status);
        Assert.Null(next.Error);
        var failed = await Assert.ThrowsAsync<AggregateException>(() => viewer.DisposeAsync().AsTask());
        Assert.Contains(error, failed.Flatten().InnerExceptions);
        Assert.Same(failed, await Assert.ThrowsAsync<AggregateException>(() => viewer.DisposeAsync().AsTask()));
    }

    private static ImageFrame Frame(byte value, Action? release = null) => ImageFrame.TakeOwnership(
        new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { value }, release ?? (() => { }));

    private sealed class CountingPresenter : ICpuImagePresenter
    {
        private readonly WriteableBitmapPresenter _inner = new();
        internal int Calls;
        public ImageSource Present(DisplayBuffer pixels) { Calls++; return _inner.Present(pixels); }
        public void Dispose() => _inner.Dispose();
    }
}
