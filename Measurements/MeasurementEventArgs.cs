namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable geometry snapshot of any completed measurement.</summary>
public sealed record MeasurementSnapshot(Guid Id, MeasurementGeometry Geometry, long GeometryVersion)
{
    public MeasurementKind Kind => Geometry.Kind;
}

/// <summary>Immutable geometry and query snapshots with a live, thread-safe measurement handle.</summary>
public sealed class MeasurementEventArgs : EventArgs
{
    public MeasurementSnapshot Snapshot { get; }
    /// <summary>The live handle for updates and removal. It may already be disposed by an earlier subscriber.</summary>
    public IMeasurement Measurement { get; }
    /// <summary>Current immutable query result, or null before publication and after invalidation.</summary>
    public MeasurementResult? Result { get; }
    internal MeasurementEventArgs(MeasurementSnapshot snapshot, IMeasurement measurement, MeasurementResult? result)
    { Snapshot = snapshot; Measurement = measurement; Result = result; }
}
