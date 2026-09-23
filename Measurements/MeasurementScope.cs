using Fizzy.ImageViewer.Interfaces;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

internal sealed class MeasurementScope : IMeasurementScope
{
    private readonly IMeasurementContext _context;
    private readonly List<UIElement> _shapes = [];
    private readonly List<IDisposable> _resources = [];
    private readonly List<Action> _callbacks = [];
    private bool _disposed;
    internal bool IsComplete { get; private set; }
    internal MeasurementScope(IMeasurementContext context) => _context = context;
    public void AddShape(UIElement shape)
    {
        EnsureAlive(); ArgumentNullException.ThrowIfNull(shape);
        var index = _shapes.Count;
        _shapes.Add(shape);
        try { _context.AttachScopeShape(this, shape); }
        catch { if (_shapes.Count > index) _shapes.RemoveAt(index); throw; }
    }
    public void AddResource(IDisposable resource) { EnsureAlive(); ArgumentNullException.ThrowIfNull(resource); _resources.Add(resource); }
    public void UpdateAnchor(UIElement shape, Point anchor)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(shape);
        if (!_shapes.Contains(shape) || OverlayShapeData.Get(shape) == null)
            throw new ArgumentException("The visual must be created by Shapes and owned by this scope.", nameof(shape));
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y))
            throw new ArgumentOutOfRangeException(nameof(anchor));
        _context.UpdateAnchor(shape, anchor);
    }
    public void OnDispose(Action callback) { EnsureAlive(); ArgumentNullException.ThrowIfNull(callback); _callbacks.Add(callback); }
    public void Complete() { EnsureAlive(); IsComplete = true; }
    private void EnsureAlive() { _context.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); }
    public void Dispose()
    {
        _context.VerifyAccess();
        if (_disposed) return; _disposed = true;
        List<Exception>? errors = null;
        foreach (var callback in _callbacks.ToArray()) try { callback(); } catch (Exception ex) { (errors ??= []).Add(ex); }
        foreach (var resource in _resources.ToArray()) try { resource.Dispose(); } catch (Exception ex) { (errors ??= []).Add(ex); }
        try { _context.DetachScope(this, _shapes); } catch (Exception ex) { (errors ??= []).Add(ex); }
        _shapes.Clear(); _resources.Clear(); _callbacks.Clear();
        if (errors is { Count: > 0 }) throw new AggregateException("Measurement scope cleanup failed.", errors);
    }
}
