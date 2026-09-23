using Fizzy.ImageViewer.Measurements;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Drawing.ShapeStyle MeasurementStyle
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
            _host.Interaction.StartMeasurement(toolId);
        });
    }

    public void CancelMeasurement()
    {
        InvokeAlive(() => _host.Interaction.Cancel());
    }

}
