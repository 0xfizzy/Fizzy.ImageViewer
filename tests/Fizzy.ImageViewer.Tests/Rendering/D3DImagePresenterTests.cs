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
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreFailureIsReportedByOwnerAfterSafeCleanup(bool detachFails)
    {
        await using var viewer = new Viewer(showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var viewport = new Fizzy.ImageViewer.Viewport.ImageViewport();
            var owner = new FramePresentation(viewer.Host.Window.Dispatcher, viewport,
                new WriteableBitmapPresenter(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            var target = new Target();
            var failures = new List<Exception>();
            var presenter = new D3DImagePresenter(target, error =>
            {
                failures.Add(error);
                owner.RecordFrontBufferFailure(error);
            });
            int released = 0;
            PresentAndReleaseCaller(presenter, 1, () => released++);
            target.Fail = "bind";
            target.Restore(); // A WPF availability event must not throw into the dispatcher.
            Assert.Single(failures);
            target.Restore();
            Assert.Single(failures); // The failed presenter cannot restore or accept more work.
            using (var frame = Frame(2, () => { }))
            using (var lease = frame.Acquire())
                Assert.Throws<InvalidOperationException>(() => presenter.Present(lease));
            Assert.Equal(0, released);
            target.Fail = detachFails ? "detach" : null;
            if (detachFails) Assert.Throws<InvalidOperationException>(presenter.Dispose);
            else presenter.Dispose();
            Assert.Equal(detachFails ? 0 : 1, released);
            // The error survives removal of the D3D presenter (for example a CPU switch).
            Assert.Same(failures[0], Assert.Throws<InvalidOperationException>(owner.Dispose));
            owner.Dispose();
            presenter.Dispose();
        });
    }

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
            var presenter = new D3DImagePresenter(target, _ => { });
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
            var presenter = new D3DImagePresenter(target, _ => { });
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
            var presenter = new D3DImagePresenter(target, _ => { });
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
        D3D9ImageFrame.TakeOwnership(new(1, 1, 4, FramePixelFormat.Bgra32), surface, new Pixels(), release);

    private sealed class Pixels : IFramePixelSource
    {
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> points, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Target : ID3DImageTarget
    {
        public string? Fail;
        public nint Surface;
        public int DetachCalls;
        public ImageSource Image { get; } = new DrawingImage();
        public bool IsFrontBufferAvailable => true;
        public event DependencyPropertyChangedEventHandler? FrontBufferAvailableChanged;
        public void Restore() => FrontBufferAvailableChanged?.Invoke(this, default);
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
