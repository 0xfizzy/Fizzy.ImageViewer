using Fizzy.ImageViewer.Interfaces;

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

    public void RegisterMeasureMethod(IMeasureMethod method)
    {
        InvokeAlive(() => _measureManager!.RegisterMethod(method));
    }

    public bool UnregisterMeasureMethod(string toolId) => InvokeAlive(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        if (_measureManager!.ActiveId == toolId) _interaction!.Cancel();
        return _measureManager.UnregisterMethod(toolId);
    });

    public void StartMeasure(string toolId)
    {
        InvokeAlive(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
            if (!_measureManager!.HasTool(toolId)) throw new KeyNotFoundException($"Unknown measurement tool '{toolId}'.");
            if (!Layers.Measurements.IsVisible) throw new InvalidOperationException("Measurement layer is hidden.");
            _interaction!.StartMeasurement(toolId);
        });
    }

    public void CancelMeasure()
    {
        InvokeAlive(() => _interaction!.Cancel());
    }

}
