using System.Windows;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer;

/// <summary>Immutable image-coordinate snapshot of a completed built-in measurement.
/// Point uses Start; line uses Start/End; rectangle uses normalized opposite corners.</summary>
public sealed record MeasurementSnapshot(Guid Id, ShapeType Kind, Point Start, Point End);

/// <summary>Built-in measurement notification. Delivered on the viewer STA.
/// Handle removes the measurement from any thread; disposal is idempotent, including after closure.
/// Snapshot is captured at notification time and does not change during subsequent editing.</summary>
public sealed class MeasurementEventArgs : EventArgs
{
    public MeasurementSnapshot Snapshot { get; }
    public IDisposable Handle { get; }
    internal MeasurementEventArgs(MeasurementSnapshot snapshot, IDisposable handle)
    { Snapshot = snapshot; Handle = handle; }
}
