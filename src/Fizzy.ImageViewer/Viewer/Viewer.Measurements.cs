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

    /// <summary>Raised once after a measurement completes, on the viewer STA. Previews are excluded.</summary>
    /// <remarks>Subscribers may start a new measurement, including the same tool, before returning.</remarks>
    public event EventHandler<MeasurementEventArgs>? MeasurementCompleted;
    /// <summary>Raised when a previously completed measurement is removed, including clear and closure.</summary>
    public event EventHandler<MeasurementEventArgs>? MeasurementRemoved;

    /// <summary>Geometry or query state changed on a completed measurement. Runs on the viewer STA.</summary>
    public event EventHandler<MeasurementEventArgs>? MeasurementChanged;

    internal void NotifyMeasurementChanged(MeasurementEventArgs args) => NotifyMeasurement(MeasurementChanged, args);
    internal void NotifyMeasurementCompleted(MeasurementEventArgs args) => NotifyMeasurement(MeasurementCompleted, args);
    internal void NotifyMeasurementRemoved(MeasurementEventArgs args) => NotifyMeasurement(MeasurementRemoved, args);

    private void NotifyMeasurement(EventHandler<MeasurementEventArgs>? handlers, MeasurementEventArgs args)
    {
        foreach (EventHandler<MeasurementEventArgs> handler in handlers?.GetInvocationList() ?? [])
            try { handler(this, args); }
            catch (Exception error) { _logger.LogWarning(error, "Measurement subscriber failed"); }
    }

}
