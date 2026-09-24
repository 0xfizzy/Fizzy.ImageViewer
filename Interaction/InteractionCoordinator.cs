using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Measurements.Editing;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using System.Windows;

namespace Fizzy.ImageViewer.Interaction;

internal enum InteractionMode { Idle, Editing, Measuring }

/// <summary>Owns selection and every input-state transition on the viewer's UI thread.</summary>
internal sealed class InteractionCoordinator : IDisposable
{
    private readonly ViewerInputBinding _input;
    private readonly MeasurementOverlay _overlay;
    private readonly EditManager _edit;
    private readonly MeasurementToolRegistry _tools;
    private readonly MeasurementStore _measurements;
    private IMeasurementToolSession? _active;
    private MeasurementCreationSession? _session;
    private long _sessionVersion;
    internal string? ActiveId { get; private set; }
    private readonly ViewerLayers _layers;
    private bool _disposed;
    public InteractionMode Mode { get; private set; }
    public MeasurementItem? SelectedMeasurement { get; private set; }
    internal EditManager Editor => _edit;

    internal InteractionCoordinator(ViewerInputBinding input, MeasurementOverlay overlay, EditManager edit,
        MeasurementToolRegistry tools, MeasurementStore context, ViewerLayers layers)
    {
        _input = input;
        _overlay = overlay;
        _edit = edit;
        _tools = tools;
        _measurements = context;
        _layers = layers;
        layers.Measurements.Clearing += CancelForClear;
        layers.Measurements.InputPolicyChanged += InputPolicyChanged;
        context.ItemRemoving += ItemRemoving;
        input.Connect(this);
    }
    internal bool Hit(UIElement shape)
    {
        if (_disposed) return false;
        if (Mode == InteractionMode.Editing) return false;
        if (Mode == InteractionMode.Measuring) return true;
        var item = _measurements.Find(shape);
        if (item == null) return false;
        Select(item); return true;
    }
    internal void Select(MeasurementItem? item)
    {
        if (_disposed || Mode == InteractionMode.Measuring || item is null || item.IsDisposed ||
            !_measurements.Contains(item)) return;
        StopEditing();
        if (item.IsDisposed) return;
        SelectedMeasurement = item;
        _overlay.SetSelection(item.Presentation.PrimaryVisual, item.Presentation.Visuals);
    }
    internal void ClearSelection()
    {
        StopEditing();
        SelectedMeasurement = null;
        _overlay.SetSelection(null, []);
    }
    internal void StartMeasurement(string name)
    {
        if (_layers.Measurements.IsClearing) throw new InvalidOperationException("Cannot start a measurement during layer cleanup.");
        if (_disposed || !_layers.Measurements.IsVisible) return;
        var tool = _tools.Find(name);
        if (tool == null) return;
        var version = _sessionVersion;
        try
        {
            version = ++_sessionVersion;
            CancelTool();
            if (version != _sessionVersion || _disposed) return;
            ActiveId = name;
            var creation = new MeasurementCreationSession(_measurements);
            _session = creation;
            ClearSelection();
            if (version != _sessionVersion || _disposed) return;
            var active = tool.CreateSession(creation) ?? throw new InvalidOperationException("Tool returned no session.");
            if (version != _sessionVersion || _disposed)
            {
                active.Cancel();
                return;
            }
            _active = active;
            Mode = InteractionMode.Measuring;
            _layers.SuppressInput(true);
            _input.ShowMeasurementCursor();
        }
        catch { if (version == _sessionVersion) Cancel(); throw; }
    }
    internal void StartEditing(MeasurementItem? item)
    {
        if (_layers.Measurements.IsClearing) throw new InvalidOperationException("Cannot start editing during layer cleanup.");
        if (_disposed || item is null || !_measurements.Contains(item) || !_edit.CanEdit(item) ||
            !_layers.Measurements.IsVisible || !_layers.Measurements.IsHitTestVisible) return;
        if (!CancelCore()) return;
        var version = _sessionVersion;
        Select(item);
        if (version != _sessionVersion || _disposed) return;
        try { if (_edit.StartEditing(item)) Mode = InteractionMode.Editing; }
        catch { if (version == _sessionVersion) Cancel(); throw; }
    }
    internal void StopEditing()
    {
        _edit.StopEditing();
        if (Mode == InteractionMode.Editing) { Mode = InteractionMode.Idle; RestoreInput(); }
    }
    internal void Cancel()
    {
        if (_disposed) return;
        CancelCore();
    }

    internal bool UnregisterMeasurementTool(string id)
    {
        if (_disposed) return false;
        // Remove before callbacks: the outgoing registration cannot restart itself.
        // A newly registered replacement or another tool may still take ownership.
        if (!_tools.UnregisterTool(id)) return false;
        if (ActiveId == id) Cancel();
        return true;
    }
    private bool CancelCore()
    {
        var version = ++_sessionVersion;
        try { CancelTool(); }
        finally
        {
            if (version == _sessionVersion)
            {
                _edit.StopEditing();
                if (version == _sessionVersion) { Mode = InteractionMode.Idle; RestoreInput(); }
            }
        }
        return version == _sessionVersion;
    }
    private void CancelTool()
    {
        var tool = _active;
        var session = _session;
        _session = null;
        session?.End();
        _active = null;
        ActiveId = null;
        if (Mode == InteractionMode.Measuring) Mode = InteractionMode.Idle;
        try { if (tool != null && session != null) tool.Cancel(); }
        finally { session?.ClearPreviews(); }
    }
    private void RestoreInput()
    {
        _input.Restore();
        _layers.SuppressInput(false);
    }
    internal void DeleteSelected() => Delete(SelectedMeasurement);
    internal void Delete(MeasurementItem? selected)
    {
        if (_disposed || selected is null || selected.IsDisposed ||
            !_measurements.Contains(selected)) return;
        ClearSelection();
        selected.Dispose();
    }
    private void ItemRemoving(MeasurementItem item)
    {
        if (ReferenceEquals(SelectedMeasurement, item) || ReferenceEquals(_edit.EditingMeasurement, item)) ClearSelection();
    }
    internal void ImageDown(double x, double y)
    {
        if (_disposed) return;
        if (Mode == InteractionMode.Idle) { ClearSelection(); return; }
        if (Mode != InteractionMode.Measuring) return;
        var version = _sessionVersion;
        try
        {
            if (_active == null || !_active.OnClick(new(x, y)) || version != _sessionVersion) return;
            var session = _session;
            _session = null;
            session?.End();
            _active = null;
            ActiveId = null;
            Mode = InteractionMode.Idle;
            session?.ClearPreviews();
            if (version == _sessionVersion) RestoreInput();
        }
        catch { if (version == _sessionVersion) Cancel(); throw; }
    }
    internal void ImageMove(double x, double y)
    {
        if (Mode != InteractionMode.Measuring) return;
        var version = _sessionVersion;
        try { _active?.OnMouseMove(new(x, y)); } catch { if (version == _sessionVersion) Cancel(); throw; }
    }
    private void InputPolicyChanged()
    {
        if (_layers.Measurements.IsVisible && _layers.Measurements.IsHitTestVisible) return;
        try { Cancel(); }
        finally { ClearSelection(); }
    }

    private void CancelForClear()
    {
        try { Cancel(); }
        finally { ClearSelection(); }
    }
    internal bool PointerDown(FrameworkElement? shape, Point point)
    {
        if (Mode != InteractionMode.Editing && shape != null && OverlayShapeData.Get(shape) != null)
            return Hit(shape);
        return BeginDrag(point);
    }
    internal bool BeginDrag(Point point)
    {
        if (Mode != InteractionMode.Editing) return false;
        if (!_edit.BeginDrag(point, _layers.Scale)) { StopEditing(); return false; }
        if (!_input.Capture()) { _edit.EndDrag(); return false; }
        _input.ShowDragCursor();
        return true;
    }
    internal bool UpdateDrag(Point point)
    {
        if (!_edit.IsDragging) return false;
        try { _edit.UpdateDrag(point); return true; }
        catch { Cancel(); throw; }
    }
    internal bool EndDrag()
    {
        if (!_edit.IsDragging) return false;
        try { _edit.EndDrag(); }
        finally { RestoreInput(); }
        return true;
    }
    internal void LostCapture()
    {
        if (!_edit.IsDragging) return;
        try { _edit.EndDrag(); }
        finally { _input.ShowDefaultCursor(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { CancelCore(); }
        finally
        {
            try { ClearSelection(); }
            finally
            {
                _measurements.ItemRemoving -= ItemRemoving;
                _input.Dispose();
                _layers.Measurements.Clearing -= CancelForClear;
                _layers.Measurements.InputPolicyChanged -= InputPolicyChanged;
            }
        }
    }
}
