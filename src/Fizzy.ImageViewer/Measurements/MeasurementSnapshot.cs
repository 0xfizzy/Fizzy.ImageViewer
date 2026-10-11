namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable geometry snapshot of any completed measurement.</summary>
/// <param name="Id">Stable measurement identity.</param>
/// <param name="Geometry">Immutable geometry captured for the notification.</param>
/// <param name="GeometryVersion">Version of the captured geometry.</param>
/// <param name="Origin">Originating tool and activation identity.</param>
public sealed record MeasurementSnapshot(Guid Id, MeasurementGeometry Geometry, long GeometryVersion, MeasurementOrigin Origin)
{
    /// <summary>Shape kind of the captured geometry.</summary>
    public MeasurementGeometryKind Kind => Geometry.Kind;
}
