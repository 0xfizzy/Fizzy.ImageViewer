namespace Fizzy.ImageViewer.Measurements;

/// <summary>Immutable geometry snapshot of any completed measurement.</summary>
public sealed record MeasurementSnapshot(Guid Id, MeasurementGeometry Geometry, long GeometryVersion, MeasurementOrigin Origin)
{
    public MeasurementGeometryKind Kind => Geometry.Kind;
}
