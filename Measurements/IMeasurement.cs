namespace Fizzy.ImageViewer.Measurements;

/// <summary>A viewer-owned measurement handle. Operations dispatch synchronously to the viewer STA;
/// callbacks run on that STA. Dispose is idempotent and safe after viewer closure.
/// Id, Origin and IsDisposed remain readable after closure; other access requires a running viewer.</summary>
public interface IMeasurement : IDisposable
{
    Guid Id { get; }
    MeasurementOrigin Origin { get; }
    MeasurementGeometry Geometry { get; }
    long GeometryVersion { get; }
    bool IsComplete { get; }
    bool IsDisposed { get; }
    MeasurementQueryResult? QueryResult { get; }
    event Action<MeasurementGeometry>? GeometryChanged;
    event Action<MeasurementQueryResult?>? QueryResultChanged;
    /// <summary>Updates the model, visuals and editing controls before notification.
    /// During dragging, programmatic changes replace the drag baseline.</summary>
    void UpdateGeometry(MeasurementGeometry geometry);
    void Complete();
    void AddResource(IDisposable resource);
    void OnDispose(Action callback);
}
