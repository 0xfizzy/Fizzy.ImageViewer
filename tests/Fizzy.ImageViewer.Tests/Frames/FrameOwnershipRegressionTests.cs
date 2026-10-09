using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class FrameOwnershipRegressionTests
{
    [Fact]
    public async Task ShutdownWaitsForSupersededSubmissionRelease()
    {
        var presenter = new BlockingPresenter();
        var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, false);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FrameSubmitResult>? replacement = null;
        try
        {
            var first = viewer.SubmitFrameAsync(Frame()).AsTask();
            await presenter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var replaced = viewer.SubmitFrameAsync(Frame(() => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(15)); })).AsTask();
            replacement = Task.Run(async () => await viewer.SubmitFrameAsync(Frame()));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            presenter.Release.Set();
            var closing = viewer.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<TimeoutException>(() => closing.WaitAsync(TimeSpan.FromMilliseconds(200)));
            Assert.False(replaced.IsCompleted);
            Assert.False(replacement.IsCompleted);
        }
        finally { release.Set(); presenter.Release.Set(); if (replacement != null) await replacement; await viewer.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false, FrameSubmitStatus.Cancelled)]
    [InlineData(true, FrameSubmitStatus.Frozen)]
    public async Task ShutdownWaitsForRejectedSubmissionAdmittedBeforeStopping(bool frozen, FrameSubmitStatus expected)
    {
        var viewer = new Viewer(showWindow: false);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FrameSubmitResult>? submission = null;
        try
        {
            if (frozen)
                await viewer.Host.Window.Dispatcher.InvokeAsync(() => viewer.Host.Pipeline.FreezeAndAcquire()?.Dispose());
            submission = Task.Run(async () => await viewer.SubmitFrameAsync(Frame(() =>
            {
                entered.SetResult();
                release.Wait(TimeSpan.FromSeconds(15));
            }), ct: new CancellationToken(!frozen)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var closing = viewer.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<TimeoutException>(() => closing.WaitAsync(TimeSpan.FromMilliseconds(200)));
            Assert.False(submission.IsCompleted);
            release.Set();
            Assert.Equal(expected, (await submission.WaitAsync(TimeSpan.FromSeconds(5))).Status);
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
            if (submission != null) await submission;
            await viewer.DisposeAsync();
        }
    }

    [Fact]
    public async Task RegionResultIsReleasedIfLastSourceReleaseThrows()
    {
        var source = new RegionSource();
        var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8), (nint)1, source, () => throw new InvalidOperationException("source release"));
        var lease = frame.Acquire();
        frame.Dispose();
        var pending = lease.ReadRegionAsync(new(0, 0, 1, 1)).AsTask();
        await source.Entered.Task;
        lease.Dispose();
        source.Resume.SetResult();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal("source release", error.Message);
        Assert.Equal(1, source.RegionReleases);
        source.Returned!.Dispose();
        Assert.Equal(1, source.RegionReleases);
    }

    [Theory]
    [InlineData("region")]
    [InlineData("pixels")]
    [InlineData("statistics")]
    public async Task ProviderAndLastSourceReleaseErrorsArePreserved(string operation)
    {
        var source = new RegionSource { Fail = true };
        var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8), (nint)1, source, () => throw new InvalidOperationException("source release"));
        var lease = frame.Acquire(); frame.Dispose();
        Task pending = operation switch
        {
            "pixels" => lease.ReadPixelsAsync(new PixelCoordinate[] { new(0, 0) }).AsTask(),
            "statistics" => lease.ComputeRegionStatisticsAsync(new(0, 0, 1, 1)).AsTask(),
            _ => lease.ReadRegionAsync(new(0, 0, 1, 1)).AsTask()
        };
        await source.Entered.Task; lease.Dispose(); source.Resume.SetResult();
        var error = await Assert.ThrowsAsync<AggregateException>(() => pending);
        Assert.Collection(error.InnerExceptions,
            e => Assert.Equal("provider error", e.Message),
            e => Assert.Equal("source release", e.Message));
    }

    [Fact]
    public async Task CancellationPreservesBothRegionAndSourceReleaseFailuresOnce()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new RegionSource { FailRegionRelease = true };
        int sourceReleases = 0;
        using var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8), (nint)1, source, () =>
        {
            sourceReleases++;
            throw new InvalidOperationException("source release");
        });
        using var lease = frame.Acquire();
        frame.Dispose();
        var pending = lease.ReadRegionAsync(new(0, 0, 1, 1), cancellation.Token).AsTask();
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lease.Dispose();
        cancellation.Cancel();
        source.Resume.SetResult();

        var error = await Assert.ThrowsAsync<AggregateException>(() => pending);
        Assert.Collection(error.InnerExceptions,
            e => Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(e).CancellationToken),
            e => Assert.Equal("region release", e.Message),
            e => Assert.Equal("source release", e.Message));
        Assert.Equal(1, source.RegionReleases);
        Assert.Equal(1, sourceReleases);
        source.Returned!.Dispose();
        lease.Dispose();
        frame.Dispose();
        Assert.Equal(1, source.RegionReleases);
        Assert.Equal(1, sourceReleases);
    }

    private static ImageFrame Frame(Action? release = null) => ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 1 }, release ?? (() => { }));
    private sealed class BlockingPresenter : ICpuImagePresenter
    {
        private readonly WriteableBitmapPresenter _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public ImageSource Present(DisplayBuffer pixels) { Entered.TrySetResult(); Release.Wait(TimeSpan.FromSeconds(15)); return _inner.Present(pixels); }
        public void Dispose() => _inner.Dispose();
    }
    private sealed class RegionSource : IFramePixelSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RegionReleases;
        public bool Fail;
        public bool FailRegionRelease;
        public ImageFrame? Returned;
        public async ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> points, CancellationToken ct)
        {
            Entered.SetResult(); await Resume.Task;
            throw new ArgumentException("provider error");
        }
        public async ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct)
        {
            Entered.SetResult(); await Resume.Task;
            throw new ArgumentException("provider error");
        }
        public async ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct)
        {
            Entered.SetResult(); await Resume.Task;
            if (Fail) throw new ArgumentException("provider error");
            return Returned = ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 1 }, () =>
            {
                RegionReleases++;
                if (FailRegionRelease) throw new InvalidOperationException("region release");
            });
        }
    }
}
