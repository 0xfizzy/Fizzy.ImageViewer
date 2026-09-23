using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Editing;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

/// <summary>Executes one editing session; it never changes global input policy.</summary>
internal sealed class EditManager(OverlayLayer overlay, MeasureContext context, ShapeEditorRegistry? registry = null)
{
    private readonly ShapeEditorRegistry _registry = registry ?? ShapeEditorRegistry.CreateDefault();
    private ShapeEditSession? _session;
    private int _dragIndex = -1;
    private readonly List<UIElement> _handles = [];
    public UIElement? EditingShape { get; private set; }
    public bool IsEditing => EditingShape != null;
    public bool IsDragging => _dragIndex >= 0;
    internal IReadOnlyList<UIElement> Handles => _handles;

    internal bool CanEdit(UIElement shape)
    {
        using var session = _registry.Create(shape, context.Find(shape));
        return session != null;
    }
    internal bool StartEditing(UIElement shape)
    {
        if (EditingShape == shape) return true;
        var session = _registry.Create(shape, context.Find(shape));
        if (session == null) return false;
        StopEditing();
        EditingShape = shape; _session = session;
        try
        {
            var points = session.Points;
            for (int i = 0; i < points.Count; i++)
            {
                var handle = ControlPointHandle.CreateHandle(points[i], shape, i);
                _handles.Add(handle); overlay.AddShape(handle);
                System.Windows.Controls.Panel.SetZIndex(handle, 1000);
            }
            return true;
        }
        catch { StopEditing(); throw; }
    }
    internal bool BeginDrag(Point point, double scale)
    {
        if (EditingShape == null || _session == null) return false;
        var points = _session.Points;
        var nearest = Enumerable.Range(0, points.Count).MinBy(i => (point - points[i]).LengthSquared);
        if ((point - points[nearest]).Length * scale >= 10) return false;
        _dragIndex = nearest; _session.BeginDrag(nearest);
        return true;
    }
    internal void UpdateDrag(Point point)
    {
        if (!IsDragging || EditingShape == null || _session == null) return;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        _session.UpdateDrag(point);
        var points = _session.Points;
        for (int i = 0; i < _handles.Count; i++) overlay.UpdateAnchor(_handles[i], points[i]);
        overlay.NotifyEditing(EditingShape);
    }
    internal void EndDrag()
    {
        bool dragged = IsDragging;
        _dragIndex = -1; _session?.EndDrag();
        if (dragged && EditingShape != null) overlay.NotifyEdited(EditingShape);
    }
    internal void StopEditing()
    {
        _dragIndex = -1; _session?.EndDrag();
        EditingShape = null;
        var session = _session; _session = null;
        var handles = _handles.ToArray(); _handles.Clear();
        try { foreach (var handle in handles) overlay.RemoveVisual(handle); }
        finally { session?.Dispose(); }
    }
}
