using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer.Imaging.Queries;

/// <summary>Single-flight batches: capture on UI, sample off UI, validate and publish on UI.</summary>
internal sealed class PixelQueryScheduler : IDisposable
{
    private readonly Func<FrameLease?> _acquire;
    private readonly ILogger _logger;
    private readonly IQueryRuntime _runtime;
    private readonly IDisposable _ticks;
    private readonly Dictionary<IFrameQueryClient, State> _states = [];
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
        public bool HasResult;
        public QueryIdentity? PublishedIdentity;
    }
    // A batch captures the frame identity and descriptor together with its owned lease.
    private sealed record Entry(IFrameQueryClient Item, QueryRequest Request, State State);
    public PixelQueryOptions QueryOptions { get => _options; set { value.Validate(); _options = value; } }
    public PixelQueryMetrics QueryMetrics => new(Interlocked.Read(ref _batches), Interlocked.Read(ref _expired), Volatile.Read(ref _duration));
    public Task Completion { get; private set; } = Task.CompletedTask;

    public PixelQueryScheduler(Func<FrameLease?> acquire, ILogger logger, IQueryRuntime runtime)
    {
        _acquire = acquire; _logger = logger; _runtime = runtime;
        _token = _stop.Token;
        _ticks = runtime.StartTicks(Tick);
    }
    public QuerySubscription Register(IFrameQueryClient item)
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
        var now = _runtime.Now;
        var due = new List<Entry>();
        foreach (var pair in _states.ToArray())
        {
            var item = pair.Key; var state = pair.Value;
            try
            {
                if (current == null)
                {
                    state.Valid = state.HasResult = false;
                    item.InvalidateResult(ResultInvalidation.NoFrame);
                    continue;
                }
                var request = item.Capture(current.Descriptor);
                if (request == null) { state.Valid = state.HasResult = false; item.InvalidateResult(ResultInvalidation.NoTarget); continue; }
                if (state.Identity != request.Identity || !Equals(state.Descriptor, current.Descriptor))
                {
                    bool descriptorChanged = !Equals(state.Descriptor, current.Descriptor);
                    state.Valid = false; state.Identity = request.Identity; state.Descriptor = current.Descriptor;
                    if (!item.Policy.AllowMovingResult) state.Due = TimeSpan.Zero;
                    if (descriptorChanged) state.HasResult = false;
                    item.InvalidateResult(descriptorChanged ? ResultInvalidation.DescriptorChanged : ResultInvalidation.CoordinatesChanged);
                }
                bool needsUpdate = !state.Valid || state.Frame != current.Info.FrameId || state.PublishedIdentity != request.Identity;
                if (state.HasResult && needsUpdate && now - state.Started > (item.Policy.DisplayAge ?? _options.MaxResultAge))
                { state.Valid = state.HasResult = false; item.InvalidateResult(ResultInvalidation.Expired); }
                if (now >= state.Due && needsUpdate) due.Add(new(item, request, state));
            }
            catch (Exception ex) { state.Valid = false; _logger.LogWarning(ex, "Pixel query capture failed"); }
        }
        if (current == null || !Completion.IsCompleted || due.Count == 0) return;
        due.Sort((a, b) => a.State.Due.CompareTo(b.State.Due));
        foreach (var entry in due)
        {
            entry.State.Due = now + Interval(entry);
        }
        Completion = RunAsync(current.Acquire(), due, now);
    }

    private TimeSpan Interval(Entry entry)
    {
        var rate = entry.Request switch { PixelQueryRequest => _options.PixelRate, LineProfileQueryRequest => _options.LineRate, _ => _options.RegionRate };
        if (entry.Item.Policy.MaximumRate is double maximum) rate = Math.Min(rate, maximum);
        return TimeSpan.FromSeconds(1 / rate);
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
        catch (Exception ex) { _logger.LogWarning(ex, "Pixel query batch failed"); }
        finally { frame.Dispose(); }
    }

    private void Publish(FrameLease frame, List<Entry> entries, QueryResult[] results, TimeSpan started)
    {
        if (_disposed) return;
        using var current = _acquire();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!_states.TryGetValue(entry.Item, out var state) || !ReferenceEquals(state, entry.State)) continue;
            if (entry.Item.Policy.AllowMovingResult) state.Due = _runtime.Now + Interval(entry);
            try
            {
                if (current == null)
                {
                    state.Valid = state.HasResult = false;
                    entry.Item.InvalidateResult(ResultInvalidation.NoFrame);
                    continue;
                }
                var latest = entry.Item.Capture(current.Descriptor);
                if (latest == null || !Equals(current.Descriptor, frame.Descriptor)) continue;
                bool sameSession = latest.Identity.ClientId == entry.Request.Identity.ClientId &&
                    latest.Identity.SessionVersion == entry.Request.Identity.SessionVersion;
                if (!sameSession || (!entry.Item.Policy.AllowMovingResult && latest.Identity != entry.Request.Identity)) continue;
                if (_runtime.Now - started > _options.MaxResultAge)
                { Interlocked.Increment(ref _expired); state.Valid = state.HasResult = false; entry.Item.InvalidateResult(ResultInvalidation.Expired); continue; }
                if (results[i] is FailedQueryResult) { state.Valid = state.HasResult = false; entry.Item.InvalidateResult(ResultInvalidation.Failed); continue; }
                // Preserve bounded-age publication during live video; requiring the newest
                // FrameId here would starve every query slower than the frame rate.
                switch (entry.Request, results[i])
                {
                    case (PixelQueryRequest p, SamplesResult s): p.Publish(s.Samples); break;
                    case (LineProfileQueryRequest l, SamplesResult s): l.Publish(s.Samples); break;
                    case (RegionStatisticsQueryRequest r, StatisticsResult s): r.Publish(s.Statistics); break;
                    default: throw new InvalidOperationException("Mismatched query result.");
                }
                state.Valid = state.HasResult = true; state.Frame = frame.Info.FrameId; state.Started = started;
                state.PublishedIdentity = entry.Request.Identity;
                entry.Item.ResultPublished(frame.Info.FrameId);
            }
            catch (Exception ex)
            {
                state.Valid = state.HasResult = false; _logger.LogWarning(ex, "Pixel query publication failed");
                try { entry.Item.InvalidateResult(ResultInvalidation.Failed); }
                catch (Exception clearError) { _logger.LogWarning(clearError, "Pixel query result cleanup failed"); }
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
