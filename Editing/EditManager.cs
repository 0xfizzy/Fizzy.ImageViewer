using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Measurements;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

/// <summary>Executes one editing session; it never changes global input policy.</summary>
internal sealed class EditManager(OverlayLayer overlay)
{
    private MeasurementEditSession? _session;
    private int _dragIndex = -1;
    private readonly List<UIElement> _handles = [];
    public MeasurementItem? EditingMeasurement { get; private set; }
    public bool IsEditing => EditingMeasurement != null;
    public bool IsDragging => _dragIndex >= 0;
    internal IReadOnlyList<UIElement> Handles => _handles;

    internal bool CanEdit(MeasurementItem? item) => item is { IsDisposed: false };
    internal bool StartEditing(MeasurementItem? item)
    {
        if (item is null || !CanEdit(item)) return false;
        if (EditingMeasurement == item) return true;
        StopEditing();
        // Removing old handles may re-enter user code and remove the requested item.
        if (item.IsDisposed) return false;
        var session = new MeasurementEditSession(item);
        EditingMeasurement = item; _session = session;
        try
        {
            var points = session.Points;
            for (int i = 0; i < points.Count; i++)
            {
                var handle = ControlPointHandle.CreateHandle(points[i]);
                _handles.Add(handle); overlay.AddShape(handle);
                System.Windows.Controls.Panel.SetZIndex(handle, 1000);
            }
            return true;
        }
        catch { StopEditing(); throw; }
    }
    internal bool BeginDrag(Point point, double scale)
    {
        if (EditingMeasurement == null || _session == null) return false;
        var points = _session.Points;
        var nearest = Enumerable.Range(0, points.Count).MinBy(i => (point - points[i]).LengthSquared);
        if ((point - points[nearest]).Length * scale >= 10) return false;
        _dragIndex = nearest; _session.BeginDrag(nearest);
        return true;
    }
    internal void UpdateDrag(Point point)
    {
        if (!IsDragging || EditingMeasurement == null || _session == null) return;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        var session = _session;
        session.Update(point);
        if (!ReferenceEquals(_session, session)) return;
        var points = session.Points;
        for (int i = 0; i < _handles.Count; i++) overlay.UpdateAnchor(_handles[i], points[i]);
    }
    internal void EndDrag()
    {
        _dragIndex = -1; _session?.EndDrag();
    }
    internal void StopEditing()
    {
        _dragIndex = -1; _session?.EndDrag();
        EditingMeasurement = null;
        var session = _session; _session = null;
        var handles = _handles.ToArray(); _handles.Clear();
        try { foreach (var handle in handles) overlay.RemoveVisual(handle); }
        finally { session?.Dispose(); }
    }
}
