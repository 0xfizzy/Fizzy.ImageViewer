namespace Fizzy.ImageViewer.Measurements;

/// <summary>A model-driven measurement owned by the viewer. Operations and events use the viewer STA.</summary>
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
