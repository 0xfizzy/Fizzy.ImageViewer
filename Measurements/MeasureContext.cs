using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging;
using System.Windows;
using Fizzy.ImageViewer.Interfaces;

namespace Fizzy.ImageViewer;

/// <summary>Capability facade for measurement tools. Scheduling and ownership stay internal.</summary>
internal sealed class MeasureContext : IMeasureToolContext, IMeasurementContext
{
    private readonly OverlayLayer _layer;
    private readonly Func<FrameLease?> _acquire;
    private readonly ILogger _logger;
    private readonly MeasurementScheduler _scheduler;
    private readonly Dictionary<UIElement, MeasurementItem> _items = [];
    private readonly HashSet<MeasurementScope> _scopes = [];
    private readonly Dictionary<UIElement, MeasurementScope> _scopeVisuals = [];
    private bool _cleaningScopes;
    private bool _disposed;
    internal PixelQueryOptions QueryOptions { get => _scheduler.QueryOptions; set => _scheduler.QueryOptions = value; }
    internal PixelQueryMetrics QueryMetrics => _scheduler.QueryMetrics;
    internal Task Completion => _scheduler.Completion;
    public event Action<FrameInfo>? FrameCommitted;
    internal event Action<MeasurementItem>? ItemRemoving;
    internal event Action<MeasurementItem>? ItemCompleted;
    internal event Action<MeasurementItem>? ItemRemoved;
    public void NotifyCompleted(MeasurementItem item)
    {
        item.CompletionNotified = true;
        ItemCompleted?.Invoke(item);
    }

    internal MeasureContext(OverlayLayer layer, Func<FrameLease?> acquire, ILogger logger)
    {
        _layer = layer; _acquire = acquire; _logger = logger;
        _scheduler = new(acquire, logger, new DispatcherMeasurementRuntime(layer.Dispatcher));
        layer.RemoveRequested = RemoveShape;
        layer.ClearRequested = ClearMeasurements;
    }
    public FrameLease? AcquireCurrentFrame() => _acquire();
    /// <summary>Creates a preview owner on the viewer STA. Call Complete to retain it after the tool ends.</summary>
    public IMeasurementScope CreateScope()
    {
        VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cleaningScopes) throw new InvalidOperationException("Cannot create a measurement scope during cleanup.");
        var scope = new MeasurementScope(this); _scopes.Add(scope); return scope;
    }
    public void VerifyAccess() => _layer.Dispatcher.VerifyAccess();
    internal void AttachVisualInternal(UIElement shape) { ObjectDisposedException.ThrowIf(_disposed, this); _layer.AddShape(shape); }
    public void RemoveShape(UIElement shape)
    {
        if (_scopeVisuals.TryGetValue(shape, out var scope)) { scope.Dispose(); return; }
        if (_items.TryGetValue(shape, out var item)) item.Dispose();
        else _layer.RemoveVisual(shape);
    }
    public void UpdateAnchor(UIElement shape, Point point) => _layer.UpdateAnchor(shape, point);
    public MeasurementSubscription Register(IFrameMeasurement item) => _scheduler.Register(item);
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
        CleanupScopes(false);
        foreach (var item in _items.Values.Distinct().ToArray())
            try { item.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement cleanup failed"); }
    }
    internal void CancelUncompletedScopes()
        => CleanupScopes(true);
    private void CleanupScopes(bool previewsOnly)
    {
        var cleaning = _cleaningScopes; _cleaningScopes = true;
        try
        {
            foreach (var scope in _scopes.Where(s => !previewsOnly || !s.IsComplete).ToArray())
                try { scope.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Measurement scope cleanup failed"); }
        }
        finally { _cleaningScopes = cleaning; }
    }
    public void AttachScopeShape(MeasurementScope scope, UIElement shape)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scopeVisuals.ContainsKey(shape) || _items.ContainsKey(shape) || _layer.Canvas.Children.Contains(shape)) throw new ArgumentException("Shape is already registered.", nameof(shape));
        _scopeVisuals.Add(shape, scope);
        try { AttachVisualInternal(shape); } catch { _scopeVisuals.Remove(shape); _layer.RemoveVisual(shape); throw; }
    }
    public void DetachScope(MeasurementScope scope, IReadOnlyCollection<UIElement> shapes)
    {
        _scopes.Remove(scope);
        foreach (var shape in shapes) _scopeVisuals.Remove(shape);
        foreach (var shape in shapes)
            try { _layer.RemoveVisual(shape); } catch (Exception ex) { _logger.LogWarning(ex, "Scope visual cleanup failed"); }
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
        _scheduler.Dispose();
        ClearMeasurements();
        FrameCommitted = null;
    }
}
