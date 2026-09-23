using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Capability facade for measurement tools. Scheduling and ownership stay internal.</summary>
internal sealed class MeasurementContext : IMeasurementToolContext
{
    public Drawing.ShapeStyle Style { get; internal set; } = Drawing.ShapeStyle.Default;
    private readonly OverlayLayer _layer;
    private readonly Func<FrameLease?> _acquire;
    private readonly ILogger _logger;
    private readonly PixelQueryScheduler _scheduler;
    private readonly Dictionary<UIElement, MeasurementItem> _items = [];
    private bool _cleaning;
    private bool _disposed;
    internal bool CreationBlocked { get; set; }
    public event Action<FrameInfo>? FrameCommitted;
    internal event Action<MeasurementItem>? ItemRemoving;
    internal event Action<MeasurementItem>? ItemCompleted;
    internal event Action<MeasurementItem>? ItemRemoved;
    public void NotifyCompleted(MeasurementItem item)
    {
        item.CompletionNotified = true;
        ItemCompleted?.Invoke(item);
    }

    internal MeasurementContext(OverlayLayer layer, Func<FrameLease?> acquire, PixelQueryScheduler scheduler, ILogger logger)
    {
        _layer = layer; _acquire = acquire; _logger = logger;
        _scheduler = scheduler;
    }
    public FrameLease? AcquireCurrentFrame() => _acquire();
    public IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options = null)
    {
        VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cleaning || CreationBlocked) throw new InvalidOperationException("Cannot create a measurement during cleanup.");
        ArgumentNullException.ThrowIfNull(geometry);
        options ??= new(); options.Validate(geometry);
        var item = new MeasurementItem(this, geometry, options);
        Attach(item); return item;
    }
    internal void Notify<T>(Action<T>? handlers, T value)
    {
        foreach (Action<T> handler in handlers?.GetInvocationList() ?? [])
            try { handler(value); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement subscriber failed"); }
    }
    public void VerifyAccess() => _layer.Dispatcher.VerifyAccess();
    internal void AttachVisualInternal(UIElement shape) { ObjectDisposedException.ThrowIf(_disposed, this); _layer.AddShape(shape); }
    public void RemoveShape(UIElement shape)
    {
        if (_items.TryGetValue(shape, out var item)) item.Dispose();
        else _layer.RemoveVisual(shape);
    }
    public void UpdateAnchor(UIElement shape, Point point) => _layer.UpdateAnchor(shape, point);
    public QuerySubscription Register(IFrameQueryClient item) => _scheduler.Register(item);
    internal MeasurementItem? Find(UIElement? shape) => shape != null && _items.TryGetValue(shape, out var item) ? item : null;
    public void Attach(MeasurementItem item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var visual in item.Visuals) _items.Add(visual, item);
        try
        {
            foreach (var visual in item.Visuals)
            {
                if (item.IsDisposed) break;
                AttachVisualInternal(visual);
            }
        }
        catch { item.Dispose(); throw; }
    }
    public void Detach(MeasurementItem item)
    {
        try { ItemRemoving?.Invoke(item); }
        finally
        {
            foreach (var visual in item.Visuals) _items.Remove(visual);
            // Remove the label before the primary element; events can safely re-enter removal.
            try { _layer.RemoveVisual(item.Label); }
            finally
            {
                _layer.RemoveVisual(item.PrimaryVisual);
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
            foreach (var item in _items.Values.Distinct().ToArray())
                try { item.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement cleanup failed"); }
            _layer.ClearVisuals();
        }
        finally { _cleaning = false; }
    }
    internal MeasurementItem[] CaptureUncompletedItems()
        => _items.Values.Distinct().Where(item => !item.IsComplete).ToArray();
    internal void CancelItems(MeasurementItem[] items)
    {
        foreach (var item in items)
            try { item.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement cleanup failed"); }
    }
    internal void NotifyFrameCommitted(FrameInfo info)
    {
        foreach (Action<FrameInfo> handler in FrameCommitted?.GetInvocationList() ?? [])
            try { handler(info); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement subscriber failed"); }
    }
    internal void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        ClearMeasurements();
        FrameCommitted = null;
    }
}
