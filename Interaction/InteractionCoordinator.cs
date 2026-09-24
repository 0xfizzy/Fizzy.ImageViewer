using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements.Editing;
using Fizzy.ImageViewer.Measurements;
using System.Windows;
using System.Runtime.ExceptionServices;

namespace Fizzy.ImageViewer.Interaction;

/// <summary>Owns selection and every input-state transition on the viewer's UI thread.</summary>
internal sealed class InteractionCoordinator : IDisposable
{
    private readonly IInteractionView _input;
    private readonly MeasurementEditController _edit;
    private readonly MeasurementToolRegistry _tools;
    private readonly MeasurementCollection _measurements;
    private readonly MeasurementRuntime _runtime;
    private readonly Func<FrameLease?> _acquire;
    private sealed class Activation(MeasurementToolRegistry.Registration registration, MeasurementCreationContext context)
    {
        internal MeasurementToolRegistry.Registration Registration { get; } = registration;
        internal MeasurementCreationContext Context { get; } = context;
        internal IMeasurementToolSession? Callback { get; set; }
    }
    private Activation? _activation;
    private long _sessionVersion;
    internal string? ActiveId => _activation?.Registration.Id;
    private readonly ViewerLayers _layers;
    private bool _disposed;
    public InteractionMode Mode { get; private set; }
    public MeasurementItem? SelectedMeasurement { get; private set; }
    internal MeasurementEditController Editor => _edit;

    internal InteractionCoordinator(IInteractionView input, MeasurementEditController edit,
        MeasurementToolRegistry tools, MeasurementCollection measurements, ViewerLayers layers,
        MeasurementRuntime runtime, Func<FrameLease?> acquire)
    {
        _input = input;
        _edit = edit;
        _tools = tools;
        _measurements = measurements;
        _runtime = runtime;
        _acquire = acquire;
        _layers = layers;
        layers.Measurements.Clearing += CancelForClear;
        layers.Measurements.InputPolicyChanged += InputPolicyChanged;
        measurements.ItemRemoving += ItemRemoving;
    }
    private bool Hit(MeasurementItem item)
    {
        if (_disposed || item.IsDisposed || !_measurements.Contains(item)) return false;
        if (Mode == InteractionMode.Editing) return false;
        if (Mode == InteractionMode.Measuring) return true;
        Select(item); return true;
    }
    internal void Select(MeasurementItem? item)
    {
        if (_disposed || Mode == InteractionMode.Measuring || item is null || item.IsDisposed ||
            !_measurements.Contains(item)) return;
        StopEditing();
        if (item.IsDisposed) return;
        SelectedMeasurement = item;
        _input.SetSelection(item);
    }
    internal void ClearSelection()
    {
        StopEditing();
        SelectedMeasurement = null;
        _input.SetSelection(null);
    }
    internal void StartMeasurement(string toolId)
    {
        if (_layers.Measurements.IsClearing) throw new InvalidOperationException("Cannot start a measurement during layer cleanup.");
        if (!CanInteract) return;
        var registration = _tools.FindRegistration(toolId);
        if (registration == null) return;
        var version = _sessionVersion;
        try
        {
            _input.EndPan();
            version = ++_sessionVersion;
            CancelTool();
            if (!CanActivate(version, registration))
            {
                if (version == _sessionVersion) RestoreInput();
                return;
            }
            var context = new MeasurementCreationContext(_measurements, _runtime, _acquire,
                new MeasurementOrigin(registration.Id, Guid.NewGuid()), Finish);
            var activation = new Activation(registration, context);
            _activation = activation;
            ClearSelection();
            if (!IsCurrent(activation)) return;
            var callback = registration.Tool.CreateSession(context)
                ?? throw new InvalidOperationException("Tool returned no session.");
            if (!IsCurrent(activation))
            {
                ReleaseSession(callback, cancelled: context.WasCancelled);
                return;
            }
            activation.Callback = callback;
            Mode = InteractionMode.Measuring;
            _layers.Collection.SuppressInput(true);
            _input.ShowMeasurementCursor();
        }
        catch (Exception error) { CancelAfterFailure(error, version); throw; }
    }

    private bool CanInteract => !_disposed && !_layers.Measurements.IsClearing &&
        _layers.Measurements.IsVisible && _layers.Measurements.IsHitTestVisible;
    private bool CanActivate(long version, MeasurementToolRegistry.Registration registration)
        => version == _sessionVersion && CanInteract && _tools.Contains(registration);
    private bool IsCurrent(Activation activation)
        => !_disposed && ReferenceEquals(_activation, activation) && _tools.Contains(activation.Registration);

    private bool Finish(MeasurementCreationContext context)
    {
        var activation = _activation;
        if (activation == null || !ReferenceEquals(activation.Context, context) || !IsCurrent(activation)) return false;
        var version = _sessionVersion;
        try { EndTool(cancelled: false); }
        finally { if (version == _sessionVersion) RestoreInput(); }
        return true;
    }

    private void CancelAfterFailure(Exception error, long version)
    {
        if (version != _sessionVersion) return;
        MeasurementFailure.RethrowAfterCleanup(error, Cancel);
    }
    internal void StartEditing(MeasurementItem? item)
    {
        if (_layers.Measurements.IsClearing) throw new InvalidOperationException("Cannot start editing during layer cleanup.");
        if (!CanInteract || item is null || !_measurements.Contains(item) || !_edit.CanEdit(item)) return;
        if (!CancelCore()) return;
        var version = _sessionVersion;
        Select(item);
        if (version != _sessionVersion || _disposed) return;
        try { if (_edit.StartEditing(item)) Mode = InteractionMode.Editing; }
        catch (Exception error) { CancelAfterFailure(error, version); throw; }
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
    private void CancelTool() => EndTool(cancelled: true);

    private void EndTool(bool cancelled)
    {
        var activation = _activation;
        _activation = null;
        activation?.Context.End(cancelled);
        if (Mode == InteractionMode.Measuring) Mode = InteractionMode.Idle;
        try { if (activation?.Callback is { } callback) ReleaseSession(callback, cancelled); }
        finally { activation?.Context.ClearPreviews(); }
    }

    private static void ReleaseSession(IMeasurementToolSession tool, bool cancelled)
    {
        Exception? cancellationError = null;
        try { if (cancelled) tool.Cancel(); }
        catch (Exception error) { cancellationError = error; }
        try { tool.Dispose(); }
        catch (Exception disposalError) when (cancellationError != null)
        { throw new AggregateException("Tool cancellation and disposal failed.", cancellationError, disposalError); }
        if (cancellationError != null) ExceptionDispatchInfo.Capture(cancellationError).Throw();
    }
    private void RestoreInput()
    {
        _input.Restore();
        _layers.Collection.SuppressInput(false);
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
            var activation = _activation;
            if (activation?.Callback == null || activation.Callback.OnClick(new(x, y)) != MeasurementClickResult.Finish || !IsCurrent(activation)) return;
            EndTool(cancelled: false);
            if (version == _sessionVersion) RestoreInput();
        }
        catch (Exception error) { CancelAfterFailure(error, version); throw; }
    }
    internal void ImageMove(double x, double y)
    {
        if (Mode != InteractionMode.Measuring) return;
        var version = _sessionVersion;
        try { _activation?.Callback?.OnMouseMove(new(x, y)); } catch (Exception error) { CancelAfterFailure(error, version); throw; }
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
    internal bool PointerDown(MeasurementItem? item, Point point)
    {
        if (Mode != InteractionMode.Editing && item != null)
            return Hit(item);
        return BeginDrag(point);
    }
    internal bool BeginDrag(Point point)
    {
        if (Mode != InteractionMode.Editing) return false;
        if (!_edit.BeginDrag(point, _layers.Collection.Scale)) { StopEditing(); return false; }
        if (!_input.Capture()) { _edit.EndDrag(); return false; }
        _input.ShowDragCursor();
        return true;
    }
    internal bool UpdateDrag(Point point)
    {
        if (!_edit.IsDragging) return false;
        var version = _sessionVersion;
        try { _edit.UpdateDrag(point); return true; }
        catch (Exception error) { CancelAfterFailure(error, version); throw; }
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
