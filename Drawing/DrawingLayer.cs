using Fizzy.ImageViewer.Layers;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>A layer of image-coordinate drawings. All public operations marshal to the viewer thread.</summary>
public sealed class DrawingLayer : ViewerLayer
{
    internal DrawingVisualHost Host { get; } = new();
    private readonly List<DrawingHandle> _drawings = [];
    internal double PixelsPerDip => VisualTreeHelper.GetDpi(Host).PixelsPerDip;
    /// <summary>Raised on the viewer thread for a drawing hit. Subscriber failures are logged
    /// independently; a hit consumes the mouse event even if a subscriber fails.</summary>
    public event EventHandler<DrawingClickedEventArgs>? DrawingClicked;

    internal DrawingLayer(LayerCollection owner, string name, int zIndex, bool builtIn)
        : base(owner, name, zIndex, builtIn, hitTest: false)
    {
        Host.RenderTransform = owner.Transform;
        Host.DpiChanged = () => Redraw(scaleOnly: false);
        Root.Children.Add(Host);
        Host.MouseDown += (_, e) => e.Handled = DispatchClick(e.GetPosition(Host), e.ChangedButton);
    }
    internal bool DispatchClick(Point imagePoint, System.Windows.Input.MouseButton button)
    {
        var drawing = HitDrawing(imagePoint);
        if (drawing == null) return false;
        var handlers = DrawingClicked;
        if (handlers != null)
        {
            var args = new DrawingClickedEventArgs(drawing, imagePoint, button);
            foreach (EventHandler<DrawingClickedEventArgs> handler in handlers.GetInvocationList())
                try { handler(this, args); }
                catch (Exception error) { Owner.Logger.LogWarning(error, "Drawing click notification failed"); }
        }
        return true;
    }
    /// <summary>Creates a drawing initially containing one element.</summary>
    public DrawingHandle Add(DrawingElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Add([element]);
    }

    /// <summary>Creates one drawing from a snapshot of the elements, using one visual regardless of element count.</summary>
    public DrawingHandle Add(IEnumerable<DrawingElement> elements)
    {
        var snapshot = DrawingHandle.Snapshot(elements);
        return Owner.Invoke(() =>
        {
            EnsureAlive();
            var handle = new DrawingHandle(this, snapshot);
            handle.Commit(snapshot, Owner.Scale, PixelsPerDip);
            _drawings.Add(handle); Host.Add(handle.Visual);
            return handle;
        });
    }
    internal DrawingHandle? HitDrawing(Point imagePoint)
    {
        if (!IsVisible || !IsHitTestVisible || Owner.InputSuppressed) return null;
        var hit = VisualTreeHelper.HitTest(Host, imagePoint)?.VisualHit;
        return _drawings.FirstOrDefault(b => ReferenceEquals(b.Visual, hit));
    }
    internal override void ClearContent()
    {
        foreach (var drawing in _drawings) drawing.Invalidate();
        _drawings.Clear();
        Host.Clear();
    }
    internal void Remove(DrawingHandle handle)
    {
        if (_drawings.Remove(handle)) Host.Remove(handle.Visual);
        handle.Invalidate();
    }
    internal override void Redraw(bool scaleOnly)
    {
        foreach (var drawing in _drawings)
            if (!scaleOnly || drawing.NeedsScale)
                drawing.Commit(drawing.Elements, Owner.Scale, PixelsPerDip);
    }
    internal override void ReleaseHandlers()
    {
        DrawingClicked = null;
        Host.DpiChanged = null;
    }
}
