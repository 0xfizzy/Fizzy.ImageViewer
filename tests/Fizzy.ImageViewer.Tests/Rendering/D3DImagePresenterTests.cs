using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Rendering;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class D3DImagePresenterTests
{
    [Theory]
    [InlineData("lock")]
    [InlineData("bind")]
    [InlineData("dirty")]
    [InlineData("unlock")]
    public async Task FailedAttachmentRetainsBothSurfacesUntilSuccessfulDetach(string phase)
    {
        await using var viewer = new Viewer(showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var target = new Target();
            var presenter = new D3DImagePresenter(target);
            int released = 0;
            PresentAndReleaseCaller(presenter, 1, () => released++);
            target.Fail = phase;
            Assert.Throws<InvalidOperationException>(() => PresentAndReleaseCaller(presenter, 2, () => released++));
            Assert.Equal(0, released);
            using (var frame = Frame(3, () => released++))
            using (var lease = frame.Acquire())
                Assert.Throws<InvalidOperationException>(() => presenter.Present(lease));
            Assert.Equal(1, released);
            target.Fail = null;
            presenter.Dispose();
            Assert.Equal(3, released);
            Assert.Equal(nint.Zero, target.Surface);
            presenter.Dispose();
            Assert.Equal(3, released);
        });
    }

    [Theory]
    [InlineData("detach")]
    [InlineData("unlock")]
    public async Task FailedDetachRootsSurfacesAndNeverRetriesDestructiveCleanup(string phase)
    {
        await using var viewer = new Viewer(showWindow: false);
        int released = 0;
        var weak = await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var target = new Target();
            var presenter = new D3DImagePresenter(target);
            PresentAndReleaseCaller(presenter, 1, () => released++);
            target.Fail = "dirty";
            Assert.Throws<InvalidOperationException>(() => PresentAndReleaseCaller(presenter, 2, () => released++));
            target.Fail = phase;
            Assert.Throws<InvalidOperationException>(presenter.Dispose);
            int calls = target.DetachCalls;
            target.Fail = null;
            presenter.Dispose();
            Assert.Equal(calls, target.DetachCalls);
            return new WeakReference(presenter);
        });
        await viewer.DisposeAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(weak.IsAlive);
        Assert.Equal(0, released);
    }

    [Fact]
    public async Task DetachReleasesEveryLeaseEvenWhenFirstReleaseThrows()
    {
        await using var viewer = new Viewer(showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var target = new Target();
            var presenter = new D3DImagePresenter(target);
            var releaseFailure = new InvalidOperationException("release");
            int secondReleased = 0;
            PresentAndReleaseCaller(presenter, 1, () => throw releaseFailure);
            target.Fail = "dirty";
            Assert.Throws<InvalidOperationException>(() => PresentAndReleaseCaller(presenter, 2, () => secondReleased++));
            target.Fail = null;
            Assert.Same(releaseFailure, Assert.Throws<InvalidOperationException>(presenter.Dispose));
            Assert.Equal(1, secondReleased);
            presenter.Dispose();
            Assert.Equal(1, secondReleased);
        });
    }

    private static void PresentAndReleaseCaller(D3DImagePresenter presenter, nint surface, Action release)
    {
        using var frame = Frame(surface, release);
        using var lease = frame.Acquire();
        presenter.Present(lease);
    }

    private static ImageFrame Frame(nint surface, Action release) =>
        ImageFrame.TakeD3D9Surface(new(1, 1, 4, FramePixelFormat.Bgra32), surface, new Pixels(), release);

    private sealed class Pixels : IFramePixelSource
    {
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> points, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Target : ID3DImageTarget
    {
        public string? Fail;
        public nint Surface;
        public int DetachCalls;
        public ImageSource Image { get; } = new DrawingImage();
        public bool IsFrontBufferAvailable => true;
        public event DependencyPropertyChangedEventHandler FrontBufferAvailableChanged { add { } remove { } }
        private void Check(string phase) { if (Fail == phase) throw new InvalidOperationException(phase); }
        public void Lock() => Check("lock");
        public void SetBackBuffer(nint surface)
        {
            Surface = surface; // Failure may occur after native state changes.
            if (surface == 0) { DetachCalls++; Check("detach"); }
            else Check("bind");
        }
        public void AddDirtyRect(Int32Rect rectangle) => Check("dirty");
        public void Unlock() => Check("unlock");
    }
}
