using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Drawing;
using System.Windows;

namespace Fizzy.ImageViewer.Interaction;

internal enum InteractionMode { Idle, Editing, Measuring }

/// <summary>Owns selection and every input-state transition on the viewer's UI thread.</summary>
internal sealed class InteractionCoordinator : IDisposable
{
    private readonly ViewerInputBinding _input;
    private readonly OverlayLayer _overlay;
    private readonly EditManager _edit;
    private readonly MeasurementToolRegistry _tools;
    private readonly MeasurementContext _context;
    private IMeasurementTool? _active;
    private MeasurementCreationSession? _session;
    private long _sessionVersion;
    internal string? ActiveId { get; private set; }
    private readonly ViewerLayers _layers;
    private bool _disposed, _clearing;
    public InteractionMode Mode { get; private set; }
    public MeasurementItem? SelectedMeasurement { get; private set; }
    internal EditManager Editor => _edit;

    internal InteractionCoordinator(ViewerInputBinding input, OverlayLayer overlay, EditManager edit,
        MeasurementToolRegistry tools, MeasurementContext context, ViewerLayers layers)
    {
        _input = input;
        _overlay = overlay;
        _edit = edit;
        _tools = tools;
        _context = context;
        _layers = layers;
        layers.Measurements.Clearing += ClearMeasurements;
        layers.Measurements.InputPolicyChanged += InputPolicyChanged;
        context.ItemRemoving += ItemRemoving;
        input.Connect(this);
    }
    internal bool Hit(UIElement shape)
    {
        if (_disposed) return false;
        if (Mode == InteractionMode.Editing) return false;
        if (Mode == InteractionMode.Measuring) return true;
        var item = _context.Find(shape);
        if (item == null) return false;
        Select(item); return true;
    }
    internal void Select(MeasurementItem? item)
    {
        if (_disposed || Mode == InteractionMode.Measuring || item is null || item.IsDisposed ||
            !_context.Contains(item)) return;
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
        if (_clearing) throw new InvalidOperationException("Cannot start a measurement during layer cleanup.");
        if (_disposed || !_layers.Measurements.IsVisible) return;
        var tool = _tools.Find(name);
        if (tool == null) return;
        var version = _sessionVersion;
        try
        {
            version = ++_sessionVersion;
            CancelTool();
            if (version != _sessionVersion || _disposed) return;
            _active = tool;
            ActiveId = name;
            _session = new MeasurementCreationSession(_context);
            _context.CurrentSession = _session;
            ClearSelection();
            if (version != _sessionVersion || _disposed) return;
            Mode = InteractionMode.Measuring;
            _layers.SuppressInput(true);
            _input.ShowMeasurementCursor();
        }
        catch { if (version == _sessionVersion) Cancel(); throw; }
    }
    internal void StartEditing(MeasurementItem? item)
    {
        if (_clearing) throw new InvalidOperationException("Cannot start editing during layer cleanup.");
        if (_disposed || item is null || !_context.Contains(item) || !_edit.CanEdit(item) ||
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
        try { if (tool != null && session != null) tool.Cancel(session); }
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
            !_context.Contains(selected)) return;
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
            if (_active == null || !_active.OnClick(new(x, y), _session!) || version != _sessionVersion) return;
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
        try { _active?.OnMouseMove(new(x, y), _session!); } catch { if (version == _sessionVersion) Cancel(); throw; }
    }
    private void InputPolicyChanged()
    {
        if (_layers.Measurements.IsVisible && _layers.Measurements.IsHitTestVisible) return;
        try { Cancel(); }
        finally { ClearSelection(); }
    }

    private void ClearMeasurements()
    {
        if (_clearing) return;
        _clearing = true;
        _context.CreationBlocked = true;
        try { Cancel(); }
        finally
        {
            try { ClearSelection(); _context.ClearMeasurements(); }
            finally { _clearing = false; _context.CreationBlocked = false; }
        }
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
        _context.CreationBlocked = true;
        try { CancelCore(); }
        finally
        {
            try { ClearSelection(); }
            finally
            {
                _context.ItemRemoving -= ItemRemoving;
                _input.Dispose();
                _layers.Measurements.Clearing -= ClearMeasurements;
                _layers.Measurements.InputPolicyChanged -= InputPolicyChanged;
            }
        }
    }
}
