using Fizzy.ImageViewer.Interfaces;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
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

    public void StartMeasure(string methodName)
    {
        InvokeAlive(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
            if (!_measureManager!.HasMethod(methodName)) throw new KeyNotFoundException($"Unknown measurement tool '{methodName}'.");
            if (!Layers.Measurements.IsVisible) throw new InvalidOperationException("Measurement layer is hidden.");
            _interaction!.StartMeasurement(methodName);
        });
    }

    public void CancelMeasure()
    {
        InvokeAlive(() => _interaction!.Cancel());
    }

    public Fizzy.ImageViewer.Imaging.PixelQueryOptions QueryOptions
    {
        get => InvokeAlive(() => _measureManager!.Context.QueryOptions);
        set => InvokeAlive(() => _measureManager!.Context.QueryOptions = value);
    }
    public Fizzy.ImageViewer.Imaging.PixelQueryMetrics QueryMetrics => InvokeAlive(() => _measureManager!.Context.QueryMetrics);
}
