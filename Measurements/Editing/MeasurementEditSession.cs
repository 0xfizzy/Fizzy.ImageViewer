using System.Windows;

namespace Fizzy.ImageViewer.Measurements.Editing;

/// <summary>Observes model geometry and preserves the drag anchor until an external update rebases it.</summary>
internal sealed class MeasurementEditSession : IDisposable
{
    private readonly MeasurementItem _item;
    private MeasurementGeometry? _original;
    private MeasurementGeometry? _pending;
    private int _index;
    public bool IsDragging => _original != null;
    public IReadOnlyList<Point> Points => _item.Geometry.ControlPoints;
    internal event Action? GeometryChanged;

    internal MeasurementEditSession(MeasurementItem item)
    {
        _item = item;
        item.GeometryApplied += OnGeometryApplied;
    }

    public void BeginDrag(int index)
    {
        _index = index;
        _original = _item.Geometry;
    }

    public void Update(Point point)
    {
        if (_original == null) return;
        _pending = _original.MoveControlPoint(_index, point);
        try { _item.UpdateGeometry(_pending); }
        finally { _pending = null; }
    }

    private void OnGeometryApplied(MeasurementGeometry geometry)
    {
        // Consume our write before any subscriber can reenter with an external update.
        if (ReferenceEquals(geometry, _pending)) _pending = null;
        else if (IsDragging) _original = geometry;
        GeometryChanged?.Invoke();
    }

    public void EndDrag() => _original = null;
    public void Dispose()
    {
        EndDrag();
        _item.GeometryApplied -= OnGeometryApplied;
        GeometryChanged = null;
    }
}
