using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Measurements;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.ExceptionServices;

namespace Fizzy.ImageViewer.Layers;

/// <summary>Owns business layers. Image and HUD surfaces are not business layers.</summary>
public sealed class ViewerLayers
{
    private readonly Dispatcher _dispatcher;
    private readonly ViewerLifetime _lifetime;
    private readonly List<ViewerLayer> _layers = [];
    private volatile bool _closed;
    private bool _redrawPending, _clearing;
    internal Grid Root { get; } = new() { Background = null };
    internal Transform Transform { get; }
    internal double Scale { get; private set; } = 1;
    internal bool InputSuppressed { get; private set; }
    public DrawingLayer Markers { get; }
    public MeasurementLayer Measurements { get; }
    public IReadOnlyList<ViewerLayer> Items => Invoke(() => (IReadOnlyList<ViewerLayer>)_layers.ToArray());
    internal ViewerLayers(Transform transform, ViewerLifetime? lifetime = null)
    {
        _lifetime = lifetime ?? new ViewerLifetime();
        _dispatcher = Root.Dispatcher; Transform = transform;
        Markers = Add("Markers", 0, true);
        Measurements = new MeasurementLayer(this);
        _layers.Add(Measurements);
        Root.Children.Add(Measurements.Root);
    }
    private DrawingLayer Add(string name, int zIndex, bool builtIn)
    {
        var layer = new DrawingLayer(this, name, zIndex, builtIn);
        _layers.Add(layer); Root.Children.Add(layer.Root); return layer;
    }
    public DrawingLayer CreateLayer(string name) => Invoke(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_layers.Any(l => l.Name == name)) throw new ArgumentException("Layer names must be unique.", nameof(name));
        return Add(name, 100, false);
    });
    public void RemoveLayer(ViewerLayer layer) => Invoke(() =>
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (!ReferenceEquals(layer.Owner, this)) throw new ArgumentException("Layer belongs to another viewer.", nameof(layer));
        if (layer.IsBuiltIn) throw new InvalidOperationException("Built-in layers cannot be removed.");
        layer.EnsureAlive(); layer.Detach(); _layers.Remove(layer); Root.Children.Remove(layer.Root);
    });
    public void Clear() => Invoke(() =>
    {
        if (_clearing) return;
        _clearing = true;
        List<Exception> errors = [];
        try
        {
            // New layers created by callbacks survive; removed layers are already cleared.
            foreach (var layer in _layers.ToArray())
                if (_layers.Contains(layer))
                    try { layer.ClearCore(); } catch (Exception error) { errors.Add(error); }
        }
        finally { _clearing = false; }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Layer cleanup failed.", errors);
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
        return _lifetime.Invoke(_dispatcher, () => { ObjectDisposedException.ThrowIf(_closed, this); return action(); });
    }
    internal void Invoke(Action action) => Invoke(() => { action(); return true; });
    internal void InvokeRemoval(Action action)
    {
        if (_closed) return;
        _lifetime.InvokeRemoval(_dispatcher, () => { if (!_closed) action(); });
    }
    internal void Close()
    {
        if (_closed) return;
        CompositionTarget.Rendering -= OnRendering; _redrawPending = false;
        foreach (var layer in _layers) layer.Detach();
        Root.Children.Clear(); _closed = true;
    }
}
