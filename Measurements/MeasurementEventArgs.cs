using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable geometry snapshot of any completed measurement.</summary>
public sealed record MeasurementSnapshot(Guid Id, MeasurementGeometry Geometry, long GeometryVersion)
{
    public MeasurementKind Kind => Geometry.Kind;
    public Point Start => Geometry.Start;
    public Point End => Geometry.End;
}

/// <summary>Immutable geometry and query state delivered on the viewer STA. The removal handle is thread-safe and idempotent.</summary>
public sealed class MeasurementEventArgs : EventArgs
{
    public MeasurementSnapshot Snapshot { get; }
    /// <summary>Removes this measurement and its resources from any thread; safe to dispose repeatedly or after closure.</summary>
    public IDisposable RemovalHandle { get; }
    /// <summary>Current immutable query result, or null before publication and after invalidation.</summary>
    public MeasurementResult? Result { get; }
    internal MeasurementEventArgs(MeasurementSnapshot snapshot, IDisposable handle, MeasurementResult? result)
    { Snapshot = snapshot; RemovalHandle = handle; Result = result; }
}
