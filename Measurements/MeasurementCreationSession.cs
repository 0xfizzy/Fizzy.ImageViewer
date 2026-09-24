using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>A tool invocation context whose unfinished measurements belong to exactly one session.</summary>
internal sealed class MeasurementCreationSession(MeasurementContext owner) : IMeasurementToolContext
{
    private readonly HashSet<MeasurementItem> _previews = [];
    private bool _ended;

    public MeasurementStyle Style => owner.Style;
    public FrameLease? AcquireCurrentFrame() => owner.AcquireCurrentFrame();

    public IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options = null)
        => owner.CreateMeasurement(geometry, options, this);

    internal void EnsureActive()
        => ObjectDisposedException.ThrowIf(_ended, this);

    internal void Track(MeasurementItem item) => _previews.Add(item);
    internal void Release(MeasurementItem item) => _previews.Remove(item);

    internal void End()
    {
        _ended = true;
    }

    internal void ClearPreviews()
    {
        var items = _previews.ToArray();
        _previews.Clear();
        owner.CancelItems(items);
    }
}
