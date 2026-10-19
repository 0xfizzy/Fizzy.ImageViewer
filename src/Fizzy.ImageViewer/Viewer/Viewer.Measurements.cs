using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc />
    public Measurements.MeasurementLayer Measurements => _host.Control.Measurements;
    /// <inheritdoc />
    public MeasurementStyle MeasurementStyle { get => _host.Control.MeasurementStyle; set => _host.Control.MeasurementStyle = value; }
    /// <inheritdoc />
    public IDisposable RegisterMeasurementTool(IMeasurementTool tool) => _host.Control.RegisterMeasurementTool(tool);
    /// <inheritdoc />
    public bool UnregisterMeasurementTool(string toolId) => _host.Control.UnregisterMeasurementTool(toolId);
    /// <inheritdoc />
    public void ActivateMeasurementTool(string toolId) => _host.Control.ActivateMeasurementTool(toolId);
    /// <inheritdoc />
    public void EndInteraction() => _host.Control.EndInteraction();

    /// <inheritdoc />
    public event EventHandler<MeasurementEventArgs>? MeasurementCompleted
    {
        add { lock (_host.Control.Runtime.Lifetime.Gate) { _host.Control.Runtime.Lifetime.ThrowIfStopping(); _measurementCompleted += value; } }
        remove { lock (_host.Control.Runtime.Lifetime.Gate) _measurementCompleted -= value; }
    }
    private EventHandler<MeasurementEventArgs>? _measurementCompleted;
    /// <inheritdoc />
    public event EventHandler<MeasurementEventArgs>? MeasurementRemoved
    {
        add { lock (_host.Control.Runtime.Lifetime.Gate) { _host.Control.Runtime.Lifetime.ThrowIfStopping(); _measurementRemoved += value; } }
        remove { lock (_host.Control.Runtime.Lifetime.Gate) _measurementRemoved -= value; }
    }
    private EventHandler<MeasurementEventArgs>? _measurementRemoved;

    /// <inheritdoc />
    public event EventHandler<MeasurementEventArgs>? MeasurementChanged
    {
        add { lock (_host.Control.Runtime.Lifetime.Gate) { _host.Control.Runtime.Lifetime.ThrowIfStopping(); _measurementChanged += value; } }
        remove { lock (_host.Control.Runtime.Lifetime.Gate) _measurementChanged -= value; }
    }
    private EventHandler<MeasurementEventArgs>? _measurementChanged;

    internal void NotifyMeasurementChanged(MeasurementEventArgs args) => NotifyMeasurement(Volatile.Read(ref _measurementChanged), args);
    internal void NotifyMeasurementCompleted(MeasurementEventArgs args) => NotifyMeasurement(Volatile.Read(ref _measurementCompleted), args);
    internal void NotifyMeasurementRemoved(MeasurementEventArgs args) => NotifyMeasurement(Volatile.Read(ref _measurementRemoved), args);

    private void NotifyMeasurement(EventHandler<MeasurementEventArgs>? handlers, MeasurementEventArgs args)
    {
        foreach (EventHandler<MeasurementEventArgs> handler in handlers?.GetInvocationList() ?? [])
            try { handler(this, args); }
            catch (Exception error) { _logger.LogWarning(error, "Measurement subscriber failed"); }
    }

}
