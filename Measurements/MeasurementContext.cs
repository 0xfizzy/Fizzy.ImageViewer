using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owns measurements and their visual lookup index; creation sessions borrow these services.</summary>
internal sealed class MeasurementContext
{
    public MeasurementStyle Style { get; internal set; } = MeasurementStyle.Default;
    private readonly MeasurementLayer _measurementLayer;
    internal OverlayLayer Layer => _measurementLayer.Overlay;
    private readonly Func<FrameLease?> _acquire;
    private readonly ILogger _logger;
    private readonly PixelQueryScheduler _scheduler;
    private readonly HashSet<MeasurementItem> _items = [];
    private readonly Dictionary<UIElement, MeasurementItem> _visualOwners = [];
    private bool _cleaning;
    private bool _disposed;
    internal event Action<MeasurementItem>? ItemRemoving;
    internal event Action<MeasurementItem>? ItemCompleted;
    internal event Action<MeasurementItem>? ItemRemoved;
    internal event Action<MeasurementItem>? ItemChanged;

    internal void NotifyChanged(MeasurementItem item)
    {
        if (item.CompletionNotified && !item.IsDisposed) ItemChanged?.Invoke(item);
    }
    public void NotifyCompleted(MeasurementItem item)
    {
        item.CompletionNotified = true;
        ItemCompleted?.Invoke(item);
    }

    internal MeasurementContext(MeasurementLayer layer, Func<FrameLease?> acquire, PixelQueryScheduler scheduler, ILogger logger)
    {
        _measurementLayer = layer;
        layer.BindContent(this);
        _acquire = acquire;
        _logger = logger;
        _scheduler = scheduler;
    }
    public FrameLease? AcquireCurrentFrame() => _acquire();
    internal IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options, MeasurementCreationSession session)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cleaning || _measurementLayer.IsClearing) throw new InvalidOperationException("Cannot create a measurement during cleanup.");
        ArgumentNullException.ThrowIfNull(session);
        session.EnsureActive();
        ArgumentNullException.ThrowIfNull(geometry);
        options ??= new();
        options.Validate(geometry);
        var item = new MeasurementItem(this, geometry, options, session);
        session.Track(item);
        Attach(item);
        return item;
    }
    internal void Notify<T>(Action<T>? handlers, T value)
    {
        foreach (Action<T> handler in handlers?.GetInvocationList() ?? [])
            try { handler(value); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement subscriber failed"); }
    }
    public void VerifyAccess() => Layer.Dispatcher.VerifyAccess();
    public QuerySubscription Register(IFrameQueryClient item) => _scheduler.Register(item);
    internal MeasurementItem? Find(UIElement? shape) => shape != null && _visualOwners.TryGetValue(shape, out var item) ? item : null;
    internal bool Contains(MeasurementItem item) => _items.Contains(item);

    private void Attach(MeasurementItem item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _items.Add(item);
        try
        {
            foreach (var visual in item.Presentation.Visuals) _visualOwners.Add(visual, item);
            item.Presentation.Attach();
        }
        catch { item.Dispose(); throw; }
    }

    public void Detach(MeasurementItem item)
    {
        try { ItemRemoving?.Invoke(item); }
        finally
        {
            _items.Remove(item);
            foreach (var visual in item.Presentation.Visuals) _visualOwners.Remove(visual);
            try { item.Presentation.Dispose(); }
            finally
            {
                if (item.CompletionNotified) ItemRemoved?.Invoke(item);
            }
        }
    }
    internal void ClearMeasurements()
    {
        if (_cleaning) return;
        _cleaning = true;
        try
        {
            foreach (var item in _items.ToArray())
                try { item.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement cleanup failed"); }
            Layer.ClearVisuals();
        }
        finally { _cleaning = false; }
    }
    internal void CancelItems(MeasurementItem[] items)
    {
        foreach (var item in items)
            try { item.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement cleanup failed"); }
    }
    internal void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        ClearMeasurements();
    }
}
