using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class PixelQuerySchedulerTests
{
    private sealed class Runtime : IQueryRuntime
    {
        public TimeSpan Now { get; set; }
        public Action? Tick;
        public readonly Queue<Func<Task>> Work = new();
        public readonly Queue<Action> Publications = new();
        public TaskCompletionSource PublicationQueued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool InWorker, InPublisher;
        public IDisposable StartTicks(Action tick) { Tick = tick; return new QuerySubscription(() => Tick = null); }
        public Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Work.Enqueue(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested(); InWorker = true;
                    var result = await action(); InWorker = false; tcs.SetResult(result);
                }
                catch (Exception ex) { tcs.SetException(ex); }
                finally { InWorker = false; }
            });
            return tcs.Task;
        }
        public Task PublishAsync(Action action, CancellationToken token)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Publications) Publications.Enqueue(() =>
            {
                try { token.ThrowIfCancellationRequested(); InPublisher = true; action(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
                finally { InPublisher = false; }
            });
            PublicationQueued.TrySetResult();
            return tcs.Task;
        }
        public async Task Finish(PixelQueryScheduler scheduler)
        {
            await Work.Dequeue()();
            await PublicationQueued.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Action publish;
            lock (Publications) publish = Publications.Dequeue();
            PublicationQueued = new(TaskCreationOptions.RunContinuationsAsynchronously);
            publish();
            await scheduler.Completion;
        }
    }
    private sealed class Client(Runtime runtime, int kind = 0) : IFrameQueryClient
    {
        public readonly Guid Id = Guid.NewGuid();
        public long Version;
        public bool Enabled = true, ThrowOnPublish;
        public int Clears, Published;
        public long FrameId;
        public QueryRequest? Capture(FrameDescriptor descriptor)
        {
            Assert.False(runtime.InWorker);
            if (!Enabled) return null;
            var identity = new QueryIdentity(Id, Version);
            return kind switch
            {
                0 => new PixelQueryRequest(identity, [new(0, 0)], _ => Publish()),
                1 => new LineProfileQueryRequest(identity, [new(0, 0), new(1, 0)], _ => Publish()),
                _ => new RegionStatisticsQueryRequest(identity, new(0, 0, 2, 1), _ => Publish())
            };
        }
        private void Publish()
        {
            Assert.True(runtime.InPublisher);
            if (ThrowOnPublish) throw new InvalidOperationException();
            Published++;
        }
        public void ClearResult() => Clears++;
        public void ResultPublished(FrameInfo frame) => FrameId = frame.FrameId;
    }
    private sealed class Source(Runtime runtime) : IFramePixelSource
    {
        public int GatherCalls, Coordinates, RegionCalls;
        public bool FailGather, FailRegion;
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct)
        {
            Assert.True(runtime.InWorker); GatherCalls++; Coordinates = coordinates.Length;
            if (FailGather) throw new InvalidOperationException();
            return ValueTask.FromResult(Enumerable.Repeat(new PixelSample(FramePixelFormat.Gray8, 7, 0, 0, 0, 255), coordinates.Length).ToArray());
        }
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct)
        {
            Assert.True(runtime.InWorker); RegionCalls++;
            if (FailRegion) throw new NotSupportedException();
            using var frame = ImageFrame.Copy(new(2, 1, 2, FramePixelFormat.Gray8), new byte[] { 7, 8 }).Transfer();
            return Compute(frame, region, ct);
        }
        private static async ValueTask<RegionStatistics> Compute(FrameLease frame, PixelRegion region, CancellationToken ct)
            => (await frame.ComputeRegionStatisticsAsync(region, ct)).Statistics;
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct) => throw new NotSupportedException();
    }
    private static FrameLease Frame(Source source, Action? release = null)
    {
        var frame = new ImageFrame(new FrameStorage(new(2, 1, 2, FramePixelFormat.Gray8), new byte[] { 7, 8 }, release ?? (() => { }), 0, source)).Transfer();
        frame.Info = new(1, frame.Descriptor, null);
        return frame;
    }

    private sealed class SliceClient(QueryRequest request) : IFrameQueryClient
    {
        public QueryRequest Capture(FrameDescriptor descriptor) => request;
        public void ClearResult() { }
    }

    [Fact]
    public async Task MergedSamplesPublishOnlyEachRequestsSlice()
    {
        var runtime = new Runtime();
        using var frame = ImageFrame.Copy(new(3, 1, 3, FramePixelFormat.Gray8), new byte[] { 11, 22, 33 }).Transfer();
        using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime);
        double[]? pixelValues = null, lineValues = null;
        var pixel = new SliceClient(new PixelQueryRequest(new(Guid.NewGuid(), 0), [new(2, 0)],
            samples => pixelValues = samples.ToArray().Select(p => p.Gray).ToArray()));
        var line = new SliceClient(new LineProfileQueryRequest(new(Guid.NewGuid(), 0), [new(0, 0), new(1, 0)],
            samples => lineValues = samples.ToArray().Select(p => p.Gray).ToArray()));
        using var a = scheduler.Register(pixel);
        using var b = scheduler.Register(line);
        runtime.Tick!();
        await runtime.Finish(scheduler);
        Assert.Equal(new double[] { 33 }, pixelValues);
        Assert.Equal(new double[] { 11, 22 }, lineValues);
    }

    [Fact]
    public async Task BatchesTypedRequestsAndHonorsIndependentRatesWithoutSleeping()
    {
        var runtime = new Runtime(); var source = new Source(runtime);
        using var frame = Frame(source);
        using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime)
        { QueryOptions = new() { PixelRate = 20, LineRate = 10, RegionRate = 5, MaxResultAge = TimeSpan.FromSeconds(5) } };
        var pixel = new Client(runtime); var line = new Client(runtime, 1); var region = new Client(runtime, 2);
        using var p = scheduler.Register(pixel); using var l = scheduler.Register(line); using var r = scheduler.Register(region);
        runtime.Tick!();
        Assert.Single(runtime.Work);
        runtime.Tick(); Assert.Single(runtime.Work); // one batch at a time
        await runtime.Finish(scheduler);
        Assert.Equal(1, source.GatherCalls); Assert.Equal(3, source.Coordinates); Assert.Equal(1, source.RegionCalls);
        Assert.Equal(1, pixel.Published); Assert.Equal(1, line.Published); Assert.Equal(1, region.Published);
        runtime.Tick(); Assert.Empty(runtime.Work); // same frame: no repeated query
        frame.Info = new(2, frame.Descriptor, null);
        runtime.Now = TimeSpan.FromMilliseconds(49); runtime.Tick(); Assert.Empty(runtime.Work);
        runtime.Now = TimeSpan.FromMilliseconds(50); runtime.Tick(); await runtime.Finish(scheduler);
        Assert.Equal(2, pixel.Published); Assert.Equal(1, line.Published); Assert.Equal(1, region.Published);
        runtime.Now = TimeSpan.FromMilliseconds(100); runtime.Tick(); await runtime.Finish(scheduler);
        Assert.Equal(2, line.Published); Assert.Equal(1, region.Published);
        runtime.Now = TimeSpan.FromMilliseconds(200); runtime.Tick(); await runtime.Finish(scheduler);
        Assert.Equal(2, region.Published); Assert.Equal(2, region.FrameId);
    }

    [Fact]
    public async Task GeometryChangeAndReRegistrationRejectInFlightResults()
    {
        var runtime = new Runtime(); using var frame = Frame(new(runtime));
        using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime);
        var changed = new Client(runtime); var removed = new Client(runtime); var disabled = new Client(runtime);
        using var a = scheduler.Register(changed); var old = scheduler.Register(removed); using var d = scheduler.Register(disabled);
        runtime.Tick!(); changed.Version++; disabled.Enabled = false; old.Dispose(); old.Dispose();
        using var replacement = scheduler.Register(removed);
        await runtime.Finish(scheduler);
        Assert.Equal(0, changed.Published); Assert.Equal(0, removed.Published); Assert.Equal(0, disabled.Published);
        runtime.Tick(); await runtime.Finish(scheduler);
        Assert.Equal(1, changed.Published); Assert.Equal(1, removed.Published);
    }

    [Fact]
    public async Task ExpiryAndQueryFailureClearResultsAndDoNotPoisonOtherRequests()
    {
        var runtime = new Runtime(); var source = new Source(runtime);
        using var frame = Frame(source);
        using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime);
        var pixel = new Client(runtime); var region = new Client(runtime, 2);
        using var p = scheduler.Register(pixel); using var r = scheduler.Register(region);
        runtime.Tick!(); runtime.Now = TimeSpan.FromMilliseconds(101); await runtime.Finish(scheduler);
        Assert.Equal(2, scheduler.QueryMetrics.ExpiredResults); Assert.Equal(0, pixel.Published);
        source.FailRegion = true;
        runtime.Tick(); await runtime.Finish(scheduler);
        Assert.Equal(1, pixel.Published); Assert.Equal(0, region.Published);
        frame.Info = new(2, frame.Descriptor, null); runtime.Now += TimeSpan.FromSeconds(1);
        source.FailRegion = false; source.FailGather = true;
        runtime.Tick(); await runtime.Finish(scheduler);
        Assert.Equal(1, pixel.Published); Assert.Equal(1, region.Published);
    }

    [Fact]
    public async Task PublicationFailureDoesNotBlockOtherSubscribers()
    {
        var runtime = new Runtime(); using var frame = Frame(new(runtime));
        using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime);
        var bad = new Client(runtime) { ThrowOnPublish = true }; var good = new Client(runtime);
        using var b = scheduler.Register(bad); using var g = scheduler.Register(good);
        runtime.Tick!(); await runtime.Finish(scheduler);
        Assert.Equal(1, good.Published); Assert.Equal(0, bad.Published);
    }

    [Fact]
    public async Task CloseCancelsQueuedWorkAndReleasesItsLeaseExactlyOnce()
    {
        var runtime = new Runtime(); int released = 0;
        var frame = Frame(new(runtime), () => released++);
        var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime);
        var client = new Client(runtime); using var subscription = scheduler.Register(client);
        runtime.Tick!(); scheduler.Dispose(); scheduler.Dispose(); frame.Dispose();
        Assert.Null(runtime.Tick); Assert.Equal(0, released);
        await runtime.Work.Dequeue()(); await scheduler.Completion;
        Assert.Equal(1, released); Assert.Equal(0, client.Published);
    }

    [Fact]
    public async Task CloseAfterExecutionCancelsQueuedPublicationAndReleasesLease()
    {
        var runtime = new Runtime(); int released = 0;
        var frame = Frame(new(runtime), () => released++);
        var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime);
        var client = new Client(runtime); using var subscription = scheduler.Register(client);
        runtime.Tick!(); await runtime.Work.Dequeue()();
        await runtime.PublicationQueued.Task.WaitAsync(TimeSpan.FromSeconds(3));
        scheduler.Dispose(); frame.Dispose();
        Assert.Equal(0, released);
        Action publish;
        lock (runtime.Publications) publish = runtime.Publications.Dequeue();
        publish(); await scheduler.Completion;
        Assert.Equal(1, released); Assert.Equal(0, client.Published);
    }

    [Fact]
    public async Task ResizedFrameRejectsOldGeometryEvenWhenVersionIsUnchanged()
    {
        var runtime = new Runtime();
        using var old = Frame(new(runtime));
        using var resized = ImageFrame.Copy(new(3, 1, 3, FramePixelFormat.Gray8), new byte[] { 1, 2, 3 }).Transfer();
        var current = old;
        using var scheduler = new PixelQueryScheduler(() => current.Acquire(), NullLogger.Instance, runtime);
        var client = new Client(runtime); using var subscription = scheduler.Register(client);
        runtime.Tick!(); current = resized;
        await runtime.Finish(scheduler); Assert.Equal(0, client.Published);
        runtime.Tick(); await runtime.Finish(scheduler); Assert.Equal(1, client.Published);
    }
    [Theory]
    [InlineData(30, 100)]
    [InlineData(5, 200)]
    public async Task PixelHudCoalescesMovementAndLimitsRateFromCompletion(double rate, int interval)
    {
        var runtime = new Runtime(); var source = new Source(runtime);
        using var frame = Frame(source);
        using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime)
        { QueryOptions = new() { PixelRate = rate } };
        string? text = null;
        var hud = new PixelInfo.PixelInfoState(value => text = value);
        hud.Enable(); hud.Move(0, 0);
        using var subscription = scheduler.Register(hud);
        runtime.Tick!();
        Assert.Contains("—", text);
        // Move while sampling: publication must retain the sampled coordinate.
        hud.Move(1, 0); runtime.Now = TimeSpan.FromMilliseconds(20); runtime.Tick();
        await runtime.Finish(scheduler);
        Assert.StartsWith("X: 0,", text); Assert.Contains("7", text);
        var previous = text;
        for (int t = 21; t < 20 + interval; t++)
        {
            runtime.Now = TimeSpan.FromMilliseconds(t);
            hud.Move(t % 2, 0); runtime.Tick();
            Assert.Empty(runtime.Work); Assert.Equal(previous, text);
        }
        hud.Move(1, 0); runtime.Now = TimeSpan.FromMilliseconds(20 + interval); runtime.Tick();
        Assert.Single(runtime.Work);
        await runtime.Finish(scheduler);
        Assert.StartsWith("X: 1,", text);
        runtime.Now += TimeSpan.FromSeconds(10); runtime.Tick();
        Assert.Empty(runtime.Work); Assert.DoesNotContain("—", text); // static result never ages out
        frame.Info = new(2, frame.Descriptor, null); runtime.Tick();
        Assert.Single(runtime.Work); await runtime.Finish(scheduler); // stationary pointer, new video frame
    }

    [Fact]
    public async Task PixelHudRetainsValueFor300MillisecondsThenRecoversFromFailure()
    {
        var runtime = new Runtime(); var source = new Source(runtime);
        using var frame = Frame(source);
        using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime)
        { QueryOptions = new() { PixelRate = 2 } };
        string? text = null;
        var hud = new PixelInfo.PixelInfoState(value => text = value);
        hud.Enable(); hud.Move(0, 0);
        using var subscription = scheduler.Register(hud);
        runtime.Tick!(); await runtime.Finish(scheduler);
        var previous = text;
        hud.Move(1, 0);
        runtime.Now = TimeSpan.FromMilliseconds(300); runtime.Tick(); Assert.Equal(previous, text);
        runtime.Now = TimeSpan.FromMilliseconds(301); runtime.Tick();
        Assert.Contains("—", text); Assert.StartsWith("X: 1,", text);
        source.FailGather = true;
        runtime.Now = TimeSpan.FromMilliseconds(500); runtime.Tick(); await runtime.Finish(scheduler);
        Assert.Contains("—", text);
        source.FailGather = false;
        runtime.Now = TimeSpan.FromMilliseconds(999); runtime.Tick(); Assert.Empty(runtime.Work);
        runtime.Now = TimeSpan.FromMilliseconds(1000); runtime.Tick(); await runtime.Finish(scheduler);
        Assert.DoesNotContain("—", text);
        hud.Move(0, 0);
        runtime.Now = TimeSpan.FromMilliseconds(1500); runtime.Tick();
        runtime.Now = TimeSpan.FromMilliseconds(1601); await runtime.Finish(scheduler);
        Assert.Contains("—", text); Assert.Equal(1, scheduler.QueryMetrics.ExpiredResults);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("disable")]
    [InlineData("outside")]
    [InlineData("reenter")]
    [InlineData("no-frame")]
    [InlineData("resize")]
    [InlineData("descriptor-roundtrip")]
    public async Task PixelHudRejectsPreviousSessionResults(string transition)
    {
        var runtime = new Runtime();
        using var frame = Frame(new(runtime));
        using var resized = ImageFrame.Copy(new(3, 1, 3, FramePixelFormat.Gray8), new byte[] { 1, 2, 3 }).Transfer();
        FrameLease? current = frame;
        using var scheduler = new PixelQueryScheduler(() => current?.Acquire(), NullLogger.Instance, runtime);
        string? text = null;
        var hud = new PixelInfo.PixelInfoState(value => text = value);
        hud.Enable(); hud.Move(0, 0);
        using var subscription = scheduler.Register(hud);
        runtime.Tick!();
        switch (transition)
        {
            case "leave": hud.Leave(); break;
            case "disable": hud.Disable(); break;
            case "outside": hud.Move(2, 0); break;
            case "reenter": hud.Leave(); hud.Move(1, 0); break;
            case "no-frame": current = null; runtime.Tick(); current = frame; break;
            case "resize": current = resized; break;
            case "descriptor-roundtrip":
                current = resized; runtime.Tick(); current = frame; break;
        }
        await runtime.Finish(scheduler);
        if (transition is "leave" or "disable" or "outside") Assert.Null(text);
        else Assert.Contains("—", text);
        // A rejected completion still observes the cooldown, including leave/re-enter.
        runtime.Now = TimeSpan.FromMilliseconds(99); runtime.Tick(); Assert.Empty(runtime.Work);
    }

}
