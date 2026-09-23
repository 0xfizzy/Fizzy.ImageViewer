using Fizzy.ImageViewer.Measurements;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Drawing.ShapeStyle MeasurementStyle
    {
        get => InvokeAlive(() => _measureManager!.Context.Style);
        set
        {
            _lifetime.ThrowIfStopping();
            ArgumentNullException.ThrowIfNull(value);
            var snapshot = value.Snapshot();
            InvokeAlive(() => _measureManager!.Context.Style = snapshot);
        }
    }

    public void RegisterMeasurementTool(IMeasurementTool tool)
    {
        InvokeAlive(() => _measureManager!.RegisterTool(tool));
    }

    public bool UnregisterMeasurementTool(string toolId) => InvokeAlive(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        if (_measureManager!.ActiveId == toolId) _interaction!.Cancel();
        return _measureManager.UnregisterTool(toolId);
    });

    public void StartMeasurement(string toolId)
    {
        InvokeAlive(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
            if (!_measureManager!.HasTool(toolId)) throw new KeyNotFoundException($"Unknown measurement tool '{toolId}'.");
            if (!Layers.Measurements.IsVisible) throw new InvalidOperationException("Measurement layer is hidden.");
            _interaction!.StartMeasurement(toolId);
        });
    }

    public void CancelMeasurement()
    {
        InvokeAlive(() => _interaction!.Cancel());
    }

}
