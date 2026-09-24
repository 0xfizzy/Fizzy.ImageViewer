namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable geometry and query snapshots with a live, thread-safe measurement handle.</summary>
public sealed class MeasurementEventArgs : EventArgs
{
    public MeasurementSnapshot Snapshot { get; }
    /// <summary>The live handle for updates and removal. It may already be disposed by an earlier subscriber.</summary>
    public IMeasurement Measurement { get; }
    /// <summary>Current immutable query result, or null before publication and after invalidation.</summary>
    public MeasurementQueryResult? QueryResult { get; }
    internal MeasurementEventArgs(MeasurementSnapshot snapshot, IMeasurement measurement, MeasurementQueryResult? result)
    { Snapshot = snapshot; Measurement = measurement; QueryResult = result; }
}
