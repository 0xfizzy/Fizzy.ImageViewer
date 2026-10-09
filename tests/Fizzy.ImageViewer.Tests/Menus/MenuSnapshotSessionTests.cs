using Fizzy.ImageViewer.Frames;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MenuSnapshotSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MenuPinReleaseFailureBelongsToSharedViewerClose(bool deferred)
    {
        var viewer = new Viewer(showWindow: false);
        var failure = new InvalidOperationException("menu pin release");
        int releases = 0, closed = 0;
        viewer.Closed += (_, _) => closed++;
        var sta = await viewer.Host.Window.Dispatcher.InvokeAsync(() => Thread.CurrentThread);
        await viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            new byte[] { 42 }, () => { releases++; throw failure; }));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => viewer.Host.MenuSession.Open());
        if (deferred)
        {
            // Retain the menu's old pin while the independently resumed pipeline commits
            // a replacement. The delayed menu cleanup then owns the final old reference.
            await viewer.Host.Window.Dispatcher.InvokeAsync(viewer.Host.Pipeline.Resume);
            await viewer.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 7 }));
            Assert.Equal(0, releases);
            await viewer.Host.Window.Dispatcher.InvokeAsync(viewer.Host.MenuSession.Close);
            await viewer.Host.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, releases);
            Assert.Equal(0, closed);
            viewer.Title = "still usable after menu cleanup failure";
            Assert.Equal("still usable after menu cleanup failure", viewer.Title);
            Assert.Equal(FrameSubmitStatus.Committed, (await viewer.SubmitFrameAsync(
                ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 8 }))).Status);
        }
        var completion = viewer.DisposeAsync().AsTask();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => completion));
        Assert.Same(completion, viewer.DisposeAsync().AsTask());
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.DisposeAsync().AsTask()));
        Assert.Equal(1, releases);
        Assert.Equal(1, closed);
        Assert.False(sta.IsAlive);
    }
}
