namespace Fizzy.ImageViewer.Measurements;

/// <summary>A viewer-owned measurement handle. Operations dispatch synchronously to the viewer STA;
/// callbacks run on that STA. Dispose is idempotent and safe after viewer closure.
/// Id, Origin and IsDisposed remain readable after closure; other access requires a running viewer.</summary>
/// <remarks>Disposal attempts query revocation, callbacks, resources and visual cleanup, aggregating failures.
/// Failed cleanup is not retried by later disposal. Independently retained geometry and query snapshots remain usable.</remarks>
public interface IMeasurement : IDisposable
{
    /// <summary>Stable identity, readable after disposal and viewer closure.</summary>
    Guid Id { get; }
    /// <summary>Immutable originating tool and activation identity, readable after viewer closure.</summary>
    MeasurementOrigin Origin { get; }
    /// <summary>Current immutable geometry in source-image pixels.</summary>
    MeasurementGeometry Geometry { get; }
    /// <summary>Version of the current geometry, used to associate query results and notifications.</summary>
    long GeometryVersion { get; }
    /// <summary>Whether this preview has been committed for retention after its tool activation ends.</summary>
    bool IsComplete { get; }
    /// <summary>Whether removal has started; remains readable after viewer closure.</summary>
    bool IsDisposed { get; }
    /// <summary>Latest immutable query result, or null before publication or after invalidation.</summary>
    MeasurementQueryResult? QueryResult { get; }
    /// <summary>Reports changed geometry on the viewer STA after model and visual updates; subscriber failures are isolated.</summary>
    event Action<MeasurementGeometry>? GeometryChanged;
    /// <summary>Reports query publication or null on invalidation on the viewer STA; subscriber failures are isolated.</summary>
    event Action<MeasurementQueryResult?>? QueryResultChanged;
    /// <summary>Updates the model, visuals and editing controls before notification.
    /// During dragging, programmatic changes replace the drag baseline.</summary>
    void UpdateGeometry(MeasurementGeometry geometry);
    /// <summary>Commits this preview once so it survives the end of its activation. Does not finish the activation or guarantee query readiness.</summary>
    void Complete();
    /// <summary>Transfers sole cleanup responsibility to this measurement on successful return.
    /// Each accepted instance is disposed once during measurement cleanup.</summary>
    /// <remarks>On failure no ownership changes: a previously unowned resource remains the caller's
    /// responsibility; a duplicate remains owned by this measurement. Do not register resources
    /// already owned by another measurement or owner.</remarks>
    /// <exception cref="ArgumentNullException">The resource is null.</exception>
    /// <exception cref="ArgumentException">This exact resource instance was already registered.</exception>
    /// <exception cref="ObjectDisposedException">The measurement or viewer has stopped accepting operations.</exception>
    void AddResource(IDisposable resource);
    /// <summary>Registers a cleanup callback for measurement removal on the viewer STA. Callback failures participate in cleanup failure reporting.</summary>
    void OnDispose(Action callback);
}
