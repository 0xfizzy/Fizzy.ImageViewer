using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>A tool invocation context whose unfinished measurements belong to exactly one session.</summary>
internal sealed class MeasurementCreationContext(MeasurementCollection owner, MeasurementOrigin? origin = null,
    Func<MeasurementCreationContext, bool>? finish = null) : IMeasurementToolContext
{
    private readonly HashSet<MeasurementItem> _previews = [];
    private bool _ended;

    public MeasurementOrigin Origin { get; } = origin ?? new("internal", Guid.NewGuid());

    public bool Finish()
    {
        var finished = false;
        owner.InvokeRemoval(() => { if (!_ended) finished = finish?.Invoke(this) ?? false; });
        return finished;
    }

    public MeasurementStyle Style => owner.Style;
    public FrameLease? AcquireCurrentFrame() => owner.AcquireCurrentFrame();

    public IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options = null)
        => owner.CreateMeasurement(geometry, options, this);

    internal void EnsureActive()
        => ObjectDisposedException.ThrowIf(_ended, this);

    internal void Track(MeasurementItem item) => _previews.Add(item);
    internal void Release(MeasurementItem item) => _previews.Remove(item);

    internal bool WasCancelled { get; private set; }
    internal void End(bool cancelled = true)
    {
        WasCancelled = cancelled;
        _ended = true;
    }

    internal void ClearPreviews()
    {
        var items = _previews.ToArray();
        _previews.Clear();
        owner.CancelItems(items);
    }
}
