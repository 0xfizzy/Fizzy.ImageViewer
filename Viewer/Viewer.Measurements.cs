using Fizzy.ImageViewer.Measurements;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public MeasurementLayer Measurements => Layers.Measurements;

    public MeasurementStyle MeasurementStyle
    {
        get => InvokeAlive(() => _host.Measurements.Style);
        set
        {
            _host.Lifetime.ThrowIfStopping();
            ArgumentNullException.ThrowIfNull(value);
            var snapshot = value.Snapshot();
            InvokeAlive(() => _host.Measurements.Style = snapshot);
        }
    }

    public void RegisterMeasurementTool(IMeasurementTool tool)
    {
        InvokeAlive(() => _host.Tools.RegisterTool(tool));
    }

    public bool UnregisterMeasurementTool(string toolId) => InvokeAlive(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        return _host.Interaction.UnregisterMeasurementTool(toolId);
    });

    public void StartMeasurement(string toolId)
    {
        InvokeAlive(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
            if (!_host.Tools.HasTool(toolId)) throw new KeyNotFoundException($"Unknown measurement tool '{toolId}'.");
            if (!Layers.Measurements.IsVisible) throw new InvalidOperationException("Measurement layer is hidden.");
            if (!Layers.Measurements.IsHitTestVisible) throw new InvalidOperationException("Measurement layer hit testing is disabled.");
            _host.Interaction.StartMeasurement(toolId);
        });
    }

    public void EndInteraction()
    {
        InvokeAlive(() => _host.Interaction.Cancel());
    }

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
            _host.Measurements.Notifications.Post(() => handler(this, args));
    }

}
