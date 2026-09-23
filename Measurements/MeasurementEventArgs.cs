using Fizzy.ImageViewer.Drawing;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable geometry snapshot of any completed measurement.</summary>
public sealed record MeasurementSnapshot(Guid Id, MeasurementGeometry Geometry, long GeometryVersion)
{
    public ShapeType Kind => Geometry.Kind;
    public Point Start => Geometry.Start;
    public Point End => Geometry.End;
}

/// <summary>Delivered on the viewer STA. The removal handle is thread-safe and idempotent.</summary>
public sealed class MeasurementEventArgs : EventArgs
{
    public MeasurementSnapshot Snapshot { get; }
    public IDisposable Handle { get; }
    internal MeasurementEventArgs(MeasurementSnapshot snapshot, IDisposable handle)
    { Snapshot = snapshot; Handle = handle; }
}
