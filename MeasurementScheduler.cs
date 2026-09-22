using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

internal sealed class MeasurementSubscription(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

internal readonly record struct QueryIdentity(Guid MeasurementId, long GeometryVersion);
internal abstract record QueryRequest(QueryIdentity Identity);
internal sealed record PixelQueryRequest(QueryIdentity Identity, PixelCoordinate[] Coordinates, Action<PixelSample[]> Publish) : QueryRequest(Identity);
internal sealed record LineProfileQueryRequest(QueryIdentity Identity, PixelCoordinate[] Coordinates, Action<PixelSample[]> Publish) : QueryRequest(Identity);
internal sealed record RegionStatisticsQueryRequest(QueryIdentity Identity, PixelRegion Region, Action<RegionStatistics> Publish) : QueryRequest(Identity);
internal abstract record QueryResult;
internal sealed record SamplesResult(PixelSample[] Samples) : QueryResult;
internal sealed record StatisticsResult(RegionStatistics Statistics) : QueryResult;
internal sealed record FailedQueryResult : QueryResult;

internal interface IFrameMeasurement
{
    QueryRequest? Capture(FrameDescriptor descriptor);
    void ClearResult();
    void ResultPublished(long frameId) { }
}

/// <summary>All time and thread boundaries are replaceable without constructing a Window.</summary>
internal interface IMeasurementRuntime
{
    TimeSpan Now { get; }
    IDisposable StartTicks(Action tick);
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token);
    Task PublishAsync(Action action, CancellationToken token);
}

internal sealed class DispatcherMeasurementRuntime(Dispatcher dispatcher) : IMeasurementRuntime
{
    private readonly long _started = Stopwatch.GetTimestamp();
    public TimeSpan Now => Stopwatch.GetElapsedTime(_started);
    public IDisposable StartTicks(Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(10) };
        EventHandler handler = (_, _) => tick();
        timer.Tick += handler; timer.Start();
        return new MeasurementSubscription(() => { timer.Stop(); timer.Tick -= handler; });
    }
    public Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token) => Task.Run(action, token);
    public Task PublishAsync(Action action, CancellationToken token) => dispatcher.InvokeAsync(action, DispatcherPriority.Background, token).Task;
}

/// <summary>Single-flight batches: capture on UI, sample off UI, validate and publish on UI.</summary>
internal sealed class MeasurementScheduler : IDisposable
{
    private readonly Func<FrameLease?> _acquire;
    private readonly ILogger _logger;
    private readonly IMeasurementRuntime _runtime;
    private readonly IDisposable _ticks;
    private readonly Dictionary<IFrameMeasurement, State> _states = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _token;
    private bool _disposed;
    private long _batches, _expired;
    private double _duration;
    private PixelQueryOptions _options = new();
    private sealed class State
    {
        public QueryIdentity? Identity;
        public FrameDescriptor? Descriptor;
        public long Frame;
        public TimeSpan Started, Due;
        public bool Valid;
    }
    // A batch captures the frame identity and descriptor together with its owned lease.
    private sealed record Entry(IFrameMeasurement Item, QueryRequest Request, State State);
    public PixelQueryOptions QueryOptions { get => _options; set { value.Validate(); _options = value; } }
    public PixelQueryMetrics QueryMetrics => new(Interlocked.Read(ref _batches), Interlocked.Read(ref _expired), Volatile.Read(ref _duration));
    public Task Completion { get; private set; } = Task.CompletedTask;

    public MeasurementScheduler(Func<FrameLease?> acquire, ILogger logger, IMeasurementRuntime runtime)
    {
        _acquire = acquire; _logger = logger; _runtime = runtime;
        _token = _stop.Token;
        _ticks = runtime.StartTicks(Tick);
    }
    public MeasurementSubscription Register(IFrameMeasurement item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_states.ContainsKey(item)) throw new InvalidOperationException("Already subscribed.");
        var state = new State(); _states.Add(item, state);
        return new(() => { if (_states.TryGetValue(item, out var current) && ReferenceEquals(current, state)) _states.Remove(item); });
    }

    internal void Tick()
    {
        if (_disposed) return;
        using var current = _acquire();
        if (current == null) return;
        var now = _runtime.Now;
        var due = new List<Entry>();
        foreach (var pair in _states.ToArray())
        {
            var item = pair.Key; var state = pair.Value;
            try
            {
                var request = item.Capture(current.Descriptor);
                if (request == null) { state.Valid = false; item.ClearResult(); continue; }
                if (state.Identity != request.Identity || !Equals(state.Descriptor, current.Descriptor))
                {
                    state.Valid = false; state.Identity = request.Identity; state.Descriptor = current.Descriptor;
                    state.Due = TimeSpan.Zero; item.ClearResult();
                }
                if (state.Valid && state.Frame != current.Info.FrameId && now - state.Started > _options.MaxResultAge)
                { state.Valid = false; item.ClearResult(); }
                if (now >= state.Due && (!state.Valid || state.Frame != current.Info.FrameId)) due.Add(new(item, request, state));
            }
            catch (Exception ex) { state.Valid = false; _logger.LogWarning(ex, "Measurement capture failed"); }
        }
        if (!Completion.IsCompleted || due.Count == 0) return;
        due.Sort((a, b) => a.State.Due.CompareTo(b.State.Due));
        foreach (var entry in due)
        {
            var rate = entry.Request switch { PixelQueryRequest => _options.PixelRate, LineProfileQueryRequest => _options.LineRate, _ => _options.RegionRate };
            entry.State.Due = now + TimeSpan.FromSeconds(1 / rate);
        }
        Completion = RunAsync(current.Acquire(), due, now);
    }

    private static PixelCoordinate[] Coordinates(QueryRequest request) => request switch
    {
        PixelQueryRequest pixel => pixel.Coordinates,
        LineProfileQueryRequest line => line.Coordinates,
        _ => []
    };

    private async Task<QueryResult[]> ExecuteBatchAsync(FrameLease frame, List<Entry> entries)
    {
        var coordinates = entries.SelectMany(e => Coordinates(e.Request)).ToArray();
        PixelSample[]? samples = [];
        try
        {
            if (coordinates.Length != 0) samples = (await frame.ReadPixelsAsync(coordinates, _token)).Samples;
            if (samples.Length != coordinates.Length) throw new InvalidOperationException("Incorrect sample count.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { samples = null; _logger.LogWarning(ex, "Pixel gather failed"); }
        int offset = 0;
        var output = new QueryResult[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            _token.ThrowIfCancellationRequested();
            var request = entries[i].Request;
            if (request is RegionStatisticsQueryRequest region)
            {
                try { output[i] = new StatisticsResult((await frame.ComputeRegionStatisticsAsync(region.Region, _token)).Statistics); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogWarning(ex, "Region statistics failed"); output[i] = new FailedQueryResult(); }
            }
            else
            {
                var count = Coordinates(request).Length;
                output[i] = samples == null ? new FailedQueryResult() : new SamplesResult(samples.AsSpan(offset, count).ToArray());
                offset += count;
            }
        }
        return output;
    }

    private async Task RunAsync(FrameLease frame, List<Entry> entries, TimeSpan started)
    {
        try
        {
            var results = await _runtime.ExecuteAsync(() => ExecuteBatchAsync(frame, entries), _token).ConfigureAwait(false);
            Volatile.Write(ref _duration, (_runtime.Now - started).TotalMilliseconds);
            Interlocked.Increment(ref _batches);
            await _runtime.PublishAsync(() => Publish(frame, entries, results, started), _token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Measurement batch failed"); }
        finally { frame.Dispose(); }
    }

    private void Publish(FrameLease frame, List<Entry> entries, QueryResult[] results, TimeSpan started)
    {
        if (_disposed) return;
        using var current = _acquire();
        if (current == null) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!_states.TryGetValue(entry.Item, out var state) || !ReferenceEquals(state, entry.State)) continue;
            try
            {
                var latest = entry.Item.Capture(current.Descriptor);
                if (latest == null || latest.Identity != entry.Request.Identity || !Equals(current.Descriptor, frame.Descriptor)) continue;
                if (_runtime.Now - started > _options.MaxResultAge)
                { Interlocked.Increment(ref _expired); state.Valid = false; entry.Item.ClearResult(); continue; }
                if (results[i] is FailedQueryResult) { state.Valid = false; entry.Item.ClearResult(); continue; }
                // Preserve bounded-age publication during live video; requiring the newest
                // FrameId here would starve every query slower than the frame rate.
                switch (entry.Request, results[i])
                {
                    case (PixelQueryRequest p, SamplesResult s): p.Publish(s.Samples); break;
                    case (LineProfileQueryRequest l, SamplesResult s): l.Publish(s.Samples); break;
                    case (RegionStatisticsQueryRequest r, StatisticsResult s): r.Publish(s.Statistics); break;
                    default: throw new InvalidOperationException("Mismatched query result.");
                }
                state.Valid = true; state.Frame = frame.Info.FrameId; state.Started = started;
                entry.Item.ResultPublished(frame.Info.FrameId);
            }
            catch (Exception ex)
            {
                state.Valid = false; _logger.LogWarning(ex, "Measurement publication failed");
                try { entry.Item.ClearResult(); }
                catch (Exception clearError) { _logger.LogWarning(clearError, "Measurement result cleanup failed"); }
            }
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _ticks.Dispose(); _stop.Cancel(); _states.Clear();
        // The token source outlives all work that may still register cancellation callbacks.
        _ = Completion.ContinueWith(_ => _stop.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
