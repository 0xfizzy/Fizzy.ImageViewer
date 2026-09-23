using Fizzy.ImageViewer.Measurements;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Drawing.ShapeStyle MeasurementStyle
    {
        get => InvokeAlive(() => _runtime.Measurements.Style);
        set
        {
            _runtime.Lifetime.ThrowIfStopping();
            ArgumentNullException.ThrowIfNull(value);
            var snapshot = value.Snapshot();
            InvokeAlive(() => _runtime.Measurements.Style = snapshot);
        }
    }

    public void RegisterMeasurementTool(IMeasurementTool tool)
    {
        InvokeAlive(() => _runtime.Tools.RegisterTool(tool));
    }

    public bool UnregisterMeasurementTool(string toolId) => InvokeAlive(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        return _runtime.Interaction.UnregisterMeasurementTool(toolId);
    });

    public void StartMeasurement(string toolId)
    {
        InvokeAlive(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
            if (!_runtime.Tools.HasTool(toolId)) throw new KeyNotFoundException($"Unknown measurement tool '{toolId}'.");
            if (!Layers.Measurements.IsVisible) throw new InvalidOperationException("Measurement layer is hidden.");
            _runtime.Interaction.StartMeasurement(toolId);
        });
    }

    public void CancelMeasurement()
    {
        InvokeAlive(() => _runtime.Interaction.Cancel());
    }

}
