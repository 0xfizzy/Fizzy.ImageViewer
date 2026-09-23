using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>A business layer in image coordinates. All public operations marshal to the viewer thread.</summary>
public sealed class DrawingLayer
{
    internal ViewerLayers Owner { get; }
    internal Grid Root { get; } = new() { Background = null };
    internal BatchVisualHost Host { get; } = new();
    private readonly List<DrawingBatchHandle> _batches = [];
    private bool _visible = true, _hitTest;
    private int _zIndex;
    private bool _removed, _clearing;
    internal event Action? Clearing;
    internal event Action? InputPolicyChanged;
    public string Name { get; }
    internal bool IsBuiltIn { get; }
    internal double PixelsPerDip => VisualTreeHelper.GetDpi(Host).PixelsPerDip;
    public event EventHandler<BatchClickedEventArgs>? BatchClicked;

    internal DrawingLayer(ViewerLayers owner, string name, int zIndex, bool builtIn, bool hitTest = false)
    {
        Owner = owner; Name = name; _zIndex = zIndex; IsBuiltIn = builtIn;
        _hitTest = hitTest;
        Host.RenderTransform = owner.Transform;
        Host.DpiChanged = () => { if (!_removed) Redraw(scaleOnly: false); };
        Root.Children.Add(Host);
        Panel.SetZIndex(Root, zIndex);
        Host.MouseDown += (_, e) =>
        {
            e.Handled = DispatchClick(e.GetPosition(Host), e.ChangedButton);
        };
        ApplyHitTest();
    }
    internal bool DispatchClick(Point imagePoint, System.Windows.Input.MouseButton button)
    {
        var batch = HitBatch(imagePoint);
        if (batch == null) return false;
        BatchClicked?.Invoke(this, new(batch, imagePoint, button));
        return true;
    }
    public bool IsVisible
    {
        get => Owner.Invoke(() => { EnsureAlive(); return _visible; });
        set => Owner.Invoke(() => { EnsureAlive(); _visible = value; Root.Visibility = value ? Visibility.Visible : Visibility.Hidden; InputPolicyChanged?.Invoke(); });
    }
    public bool IsHitTestVisible
    {
        get => Owner.Invoke(() => { EnsureAlive(); return _hitTest; });
        set => Owner.Invoke(() => { EnsureAlive(); _hitTest = value; ApplyHitTest(); InputPolicyChanged?.Invoke(); });
    }
    public int ZIndex
    {
        get => Owner.Invoke(() => { EnsureAlive(); return _zIndex; });
        set => Owner.Invoke(() => { EnsureAlive(); _zIndex = value; Panel.SetZIndex(Root, value); });
    }
    internal void EnsureAlive() => ObjectDisposedException.ThrowIf(_removed, this);
    internal void ApplyHitTest()
    {
        bool enabled = _hitTest && !Owner.InputSuppressed;
        Root.IsHitTestVisible = enabled;
    }
    public DrawingBatchHandle AddBatch(IEnumerable<DrawingElement> elements)
    {
        var snapshot = DrawingBatchHandle.Snapshot(elements);
        return Owner.Invoke(() =>
        {
            EnsureAlive();
            var handle = new DrawingBatchHandle(this, snapshot);
            handle.Commit(snapshot, Owner.Scale, PixelsPerDip);
            _batches.Add(handle); Host.Add(handle.Visual);
            return handle;
        });
    }
    internal DrawingBatchHandle? HitBatch(Point imagePoint)
    {
        if (!_visible || !_hitTest || Owner.InputSuppressed || _removed) return null;
        var hit = VisualTreeHelper.HitTest(Host, imagePoint)?.VisualHit;
        return _batches.FirstOrDefault(b => ReferenceEquals(b.Visual, hit));
    }
    public void Clear() => Owner.Invoke(() => { EnsureAlive(); ClearCore(); });
    internal void ClearCore()
    {
        if (_clearing) return;
        _clearing = true;
        try { Clearing?.Invoke(); }
        finally
        {
            foreach (var batch in _batches) batch.Invalidate();
            _batches.Clear(); Host.Clear();
            _clearing = false;
        }
    }
    internal void Remove(DrawingBatchHandle handle)
    {
        if (_batches.Remove(handle)) Host.Remove(handle.Visual);
        handle.Invalidate();
    }
    internal void Redraw(bool scaleOnly)
    {
        foreach (var batch in _batches)
            if (!scaleOnly || batch.NeedsScale)
                batch.Commit(batch.Elements, Owner.Scale, PixelsPerDip);
    }
    internal void Detach()
    {
        ClearCore(); _removed = true; BatchClicked = null; Clearing = null; InputPolicyChanged = null;
    }
}
