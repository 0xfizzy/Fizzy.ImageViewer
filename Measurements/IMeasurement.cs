namespace Fizzy.ImageViewer.Measurements;

/// <summary>A model-driven measurement owned by the viewer. All operations, including Dispose,
/// require the viewer STA. Use MeasurementEventArgs.RemovalHandle to remove an item from another thread.</summary>
public interface IMeasurement : IDisposable
{
    Guid Id { get; }
    MeasurementGeometry Geometry { get; }
    long GeometryVersion { get; }
    bool IsComplete { get; }
    bool IsDisposed { get; }
    MeasurementResult? Result { get; }
    event Action<MeasurementGeometry>? GeometryChanged;
    event Action<MeasurementResult?>? ResultChanged;
    void UpdateGeometry(MeasurementGeometry geometry);
    void Complete();
    void AddResource(IDisposable resource);
    void OnDispose(Action callback);
}
