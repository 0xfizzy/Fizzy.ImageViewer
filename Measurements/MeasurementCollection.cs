using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owns measurements and their visual lookup index; creation sessions borrow these services.</summary>
internal sealed class MeasurementCollection
{
    internal MeasurementNotificationQueue Notifications => _runtime.Notifications;
    public MeasurementStyle Style { get; internal set; } = MeasurementStyle.Default;
    private readonly MeasurementLayer _measurementLayer;
    private readonly Func<FrameLease?> _acquire;
    private readonly ILogger _logger;
    private readonly MeasurementRuntime _runtime;
    private readonly HashSet<MeasurementItem> _items = [];
    private readonly Dictionary<UIElement, MeasurementItem> _visualOwners = [];
    private bool _cleaning;
    private bool _disposed;
    internal event Action<MeasurementItem>? ItemRemoving;
    internal event Action<MeasurementEventArgs>? ItemCompleted;
    internal event Action<MeasurementEventArgs>? ItemRemoved;
    internal event Action<MeasurementEventArgs>? ItemChanged;

    internal void NotifyChanged(MeasurementItem item)
    {
        if (item.CompletionNotified && !item.IsDisposed) ItemChanged?.Invoke(Snapshot(item));
    }
    public void NotifyCompleted(MeasurementItem item)
    {
        item.CompletionNotified = true;
        ItemCompleted?.Invoke(Snapshot(item));
    }

    internal MeasurementCollection(MeasurementLayer layer, ViewerLifetime lifetime, Dispatcher dispatcher,
        Func<FrameLease?> acquire, PixelQueryScheduler scheduler, ILogger logger)
    {
        _measurementLayer = layer;
        _runtime = new(lifetime, dispatcher, scheduler, logger);
        layer.ContentClearing += ClearMeasurements;
        _acquire = acquire;
        _logger = logger;
    }
    public FrameLease? AcquireCurrentFrame() => _acquire();
    internal IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options, MeasurementCreationContext session)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cleaning || _measurementLayer.IsClearing) throw new InvalidOperationException("Cannot create a measurement during cleanup.");
        ArgumentNullException.ThrowIfNull(session);
        session.EnsureActive();
        ArgumentNullException.ThrowIfNull(geometry);
        options ??= new();
        options.Validate(geometry);
        var item = new MeasurementItem(this, _runtime, _measurementLayer.Overlay, (options.Style ?? Style).Snapshot(), geometry, options, session);
        session.Track(item);
        Attach(item);
        return item;
    }
    private static MeasurementEventArgs Snapshot(MeasurementItem item) =>
        new(new(item.Id, item.Geometry, item.GeometryVersion, item.Origin), item, item.QueryResult);
    public void VerifyAccess() => _runtime.VerifyAccess();
    internal T Invoke<T>(Func<T> action) => _runtime.Invoke(action);
    internal void Invoke(Action action) => _runtime.Invoke(action);
    internal void InvokeRemoval(Action action) => _runtime.InvokeRemoval(action);
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
        catch (Exception error) { MeasurementFailure.RethrowAfterCleanup(error, item.Dispose); throw; }
    }

    public void Detach(MeasurementItem item)
    {
        try { ItemRemoving?.Invoke(item); }
        finally
        {
            _items.Remove(item);
            foreach (var visual in item.Presentation.Visuals) _visualOwners.Remove(visual);
        }
    }
    internal void NotifyRemoved(MeasurementItem item)
    {
        if (item.CompletionNotified) ItemRemoved?.Invoke(Snapshot(item));
    }
    internal void ClearMeasurements()
    {
        if (_cleaning) return;
        _cleaning = true;
        try
        {
            foreach (var item in _items.ToArray())
                try { item.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement cleanup failed"); }
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
        _runtime.Stop();
        try { ClearMeasurements(); }
        finally { _measurementLayer.ContentClearing -= ClearMeasurements; }
    }
}
