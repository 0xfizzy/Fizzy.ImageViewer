using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>A business layer in image coordinates. All public operations marshal to the viewer thread.</summary>
public sealed class DrawingLayer : ViewerLayer
{
    internal BatchVisualHost Host { get; } = new();
    private readonly List<DrawingBatchHandle> _batches = [];
    internal double PixelsPerDip => VisualTreeHelper.GetDpi(Host).PixelsPerDip;
    public event EventHandler<BatchClickedEventArgs>? BatchClicked;

    internal DrawingLayer(ViewerLayers owner, string name, int zIndex, bool builtIn)
        : base(owner, name, zIndex, builtIn, hitTest: false)
    {
        Host.RenderTransform = owner.Transform;
        Host.DpiChanged = () => Redraw(scaleOnly: false);
        Root.Children.Add(Host);
        Host.MouseDown += (_, e) => e.Handled = DispatchClick(e.GetPosition(Host), e.ChangedButton);
    }
    internal bool DispatchClick(Point imagePoint, System.Windows.Input.MouseButton button)
    {
        var batch = HitBatch(imagePoint);
        if (batch == null) return false;
        BatchClicked?.Invoke(this, new(batch, imagePoint, button));
        return true;
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
        if (!IsVisible || !IsHitTestVisible || Owner.InputSuppressed) return null;
        var hit = VisualTreeHelper.HitTest(Host, imagePoint)?.VisualHit;
        return _batches.FirstOrDefault(b => ReferenceEquals(b.Visual, hit));
    }
    internal override void ClearContent()
    {
        foreach (var batch in _batches) batch.Invalidate();
        _batches.Clear();
        Host.Clear();
    }
    internal void Remove(DrawingBatchHandle handle)
    {
        if (_batches.Remove(handle)) Host.Remove(handle.Visual);
        handle.Invalidate();
    }
    internal override void Redraw(bool scaleOnly)
    {
        foreach (var batch in _batches)
            if (!scaleOnly || batch.NeedsScale)
                batch.Commit(batch.Elements, Owner.Scale, PixelsPerDip);
    }
    internal override void ReleaseHandlers()
    {
        BatchClicked = null;
        Host.DpiChanged = null;
    }
}
