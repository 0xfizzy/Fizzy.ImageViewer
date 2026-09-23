using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Enums;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

/// <summary>Internal bindings; each registration creates a fresh editing session.</summary>
internal sealed class ShapeEditorRegistry
{
    private readonly Dictionary<ShapeType, Func<UIElement, MeasurementItem?, ShapeEditSession>> _factories = [];

    internal void Register(ShapeType key, Func<UIElement, MeasurementItem?, ShapeEditSession> factory)
        => _factories.Add(key, factory);
    internal bool Unregister(ShapeType key) => _factories.Remove(key);
    internal ShapeEditSession? Create(UIElement shape, MeasurementItem? item)
    {
        if (shape is not FrameworkElement { Tag: OverlayShapeData data } || !_factories.TryGetValue(data.ShapeType, out var factory)) return null;
        var session = factory(shape, item);
        var points = session.Points;
        if (points.Count > 0 && points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y))) return session;
        session.Dispose();
        return null;
    }
    internal static ShapeEditorRegistry CreateDefault()
    {
        var registry = new ShapeEditorRegistry();
        registry.Register(ShapeType.Point, (shape, item) => new(shape, item, new PointEditor()));
        registry.Register(ShapeType.Line, (shape, item) => new(shape, item, new LineEditor()));
        registry.Register(ShapeType.Rectangle, (shape, item) => new(shape, item, new RectangleEditor(), RectangleEditor.CreateDrag));
        registry.Register(ShapeType.Circle, (shape, item) => new(shape, item, new CircleEditor()));
        registry.Register(ShapeType.Crosshair, (shape, item) => new(shape, item, new CrosshairEditor()));
        return registry;
    }
}

internal sealed class ShapeEditSession(UIElement shape, MeasurementItem? item, IShapeEditor editor,
    Func<UIElement, int, IReadOnlyList<Point>, Action<Point>>? createDrag = null) : IDisposable
{
    private Action<Point>? _drag;
    internal IReadOnlyList<Point> Points => item?.Geometry.ControlPoints ?? editor.GetControlPoints(shape);
    internal void BeginDrag(int index)
    {
        if (item != null)
        {
            var original = item.Geometry;
            _drag = point => item.UpdateGeometry(original.MoveControlPoint(index, point));
        }
        else _drag = createDrag?.Invoke(shape, index, Points.ToArray()) ?? (point => editor.UpdateControlPoint(shape, index, point));
    }
    internal void UpdateDrag(Point point) => _drag?.Invoke(point);
    internal void EndDrag() => _drag = null;
    public void Dispose() { EndDrag(); if (editor is IDisposable resource) resource.Dispose(); }
}
