using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Fizzy.ImageViewer.Controls;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Owns business layers. Image and HUD surfaces are not business layers.</summary>
public sealed class ViewerLayers
{
    private readonly Dispatcher _dispatcher;
    private readonly List<DrawingLayer> _layers = [];
    private volatile bool _closed;
    private bool _redrawPending;
    internal Grid Root { get; } = new() { Background = null };
    internal Transform Transform { get; }
    internal double Scale { get; private set; } = 1;
    internal bool InputSuppressed { get; private set; }
    internal Action? CancelMeasurement { get; set; }
    public DrawingLayer Markers { get; }
    public DrawingLayer Measurements { get; }
    public IReadOnlyList<DrawingLayer> Items => Invoke(() => (IReadOnlyList<DrawingLayer>)_layers.ToArray());
    internal ViewerLayers(OverlayLayer measurements, Transform transform)
    {
        _dispatcher = measurements.Dispatcher; Transform = transform;
        Markers = Add("Markers", 0, true);
        Measurements = Add("Measurements", 1000, true, measurements);
    }
    private DrawingLayer Add(string name, int zIndex, bool builtIn, OverlayLayer? measurements = null)
    {
        var layer = new DrawingLayer(this, name, zIndex, builtIn, measurements);
        _layers.Add(layer); Root.Children.Add(layer.Root); return layer;
    }
    public DrawingLayer CreateLayer(string name) => Invoke(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_layers.Any(l => l.Name == name)) throw new ArgumentException("Layer names must be unique.", nameof(name));
        return Add(name, 100, false);
    });
    public void RemoveLayer(DrawingLayer layer) => Invoke(() =>
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (!ReferenceEquals(layer.Owner, this)) throw new ArgumentException("Layer belongs to another viewer.", nameof(layer));
        if (layer.IsBuiltIn) throw new InvalidOperationException("Built-in layers cannot be removed.");
        layer.EnsureAlive(); layer.Detach(); _layers.Remove(layer); Root.Children.Remove(layer.Root);
    });
    public void Clear() => Invoke(() =>
    {
        CancelMeasurement?.Invoke();
        foreach (var layer in _layers) layer.ClearCore();
    });
    internal void SuppressInput(bool suppressed)
    {
        InputSuppressed = suppressed;
        foreach (var layer in _layers) layer.ApplyHitTest();
    }
    internal void UpdateScale(double scale)
    {
        if (_closed || Scale == scale || !double.IsFinite(scale) || scale <= 0) return;
        Scale = scale;
        if (_redrawPending) return;
        _redrawPending = true;
        CompositionTarget.Rendering += OnRendering;
    }
    private void OnRendering(object? sender, EventArgs e) => FlushScale();
    internal void FlushScale()
    {
        if (!_redrawPending) return;
        CompositionTarget.Rendering -= OnRendering; _redrawPending = false;
        if (_closed) return;
        foreach (var layer in _layers) layer.Redraw(scaleOnly: true);
    }
    internal T Invoke<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        return _dispatcher.Invoke(() => { ObjectDisposedException.ThrowIf(_closed, this); return action(); });
    }
    internal void Invoke(Action action) => Invoke(() => { action(); return true; });
    internal void InvokeRemoval(Action action)
    {
        if (_closed) return;
        try { _dispatcher.Invoke(() => { if (!_closed) action(); }); }
        catch (TaskCanceledException) when (_closed) { }
        catch (InvalidOperationException) when (_closed) { }
    }
    internal void Close()
    {
        if (_closed) return;
        CompositionTarget.Rendering -= OnRendering; _redrawPending = false;
        CancelMeasurement = null;
        foreach (var layer in _layers) layer.Detach();
        Root.Children.Clear(); _closed = true;
    }
}
