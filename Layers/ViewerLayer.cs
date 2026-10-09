using System.Windows;
using System.Windows.Controls;
using System.Runtime.ExceptionServices;

namespace Fizzy.ImageViewer.Layers;

/// <summary>Shared visibility, input and lifetime of a business layer. Operations dispatch to the viewer STA.</summary>
public abstract class ViewerLayer
{
    internal LayerCollection Owner { get; }
    internal Grid Root { get; } = new() { Background = null };
    private bool _visible = true, _hitTest;
    private int _zIndex;
    private bool _removed, _clearing;
    internal bool IsClearing => _clearing;
    internal event Action? Clearing;
    internal event Action? InputPolicyChanged;
    /// <summary>Layer name assigned at creation.</summary>
    public string Name { get; }
    internal bool IsBuiltIn { get; }

    internal ViewerLayer(LayerCollection owner, string name, int zIndex, bool builtIn, bool hitTest)
    {
        Owner = owner;
        Name = name;
        _zIndex = zIndex;
        IsBuiltIn = builtIn;
        _hitTest = hitTest;
        Panel.SetZIndex(Root, zIndex);
        ApplyHitTest();
    }

    /// <summary>Whether layer content is visible. Hiding the measurement layer cancels previews while retaining completed measurements.</summary>
    public bool IsVisible
    {
        get => Owner.Invoke(() => { EnsureAlive(); return _visible; });
        set => Owner.Invoke(() =>
        {
            EnsureAlive();
            _visible = value;
            Root.Visibility = value ? Visibility.Visible : Visibility.Hidden;
            InputPolicyChanged?.Invoke();
        });
    }
    /// <summary>Configured input policy; temporary interaction suppression may still prevent hit testing.</summary>
    public bool IsHitTestVisible
    {
        get => Owner.Invoke(() => { EnsureAlive(); return _hitTest; });
        set => Owner.Invoke(() =>
        {
            EnsureAlive();
            _hitTest = value;
            ApplyHitTest();
            InputPolicyChanged?.Invoke();
        });
    }
    /// <summary>Stacking order; larger values place this layer above layers with smaller values.</summary>
    public int ZIndex
    {
        get => Owner.Invoke(() => { EnsureAlive(); return _zIndex; });
        set => Owner.Invoke(() =>
        {
            EnsureAlive();
            _zIndex = value;
            Panel.SetZIndex(Root, value);
        });
    }
    internal void EnsureAlive() => ObjectDisposedException.ThrowIf(_removed, this);
    internal void ApplyHitTest()
    {
        bool enabled = _hitTest && !Owner.InputSuppressed;
        Root.IsHitTestVisible = enabled;
    }
    /// <summary>Clears this layer's content and cancels its interaction while retaining the layer. Cleanup failures propagate after required cleanup is attempted.</summary>
    public void Clear() => Owner.Invoke(() => { EnsureAlive(); ClearCore(); });

    internal void ClearCore()
    {
        if (_clearing) return;
        _clearing = true;
        Exception? cancellationError = null;
        try
        {
            try { Clearing?.Invoke(); }
            catch (Exception error) { cancellationError = error; }
            try { ClearContent(); }
            catch (Exception cleanupError) when (cancellationError != null)
            { throw new AggregateException(cancellationError, cleanupError); }
            if (cancellationError != null) ExceptionDispatchInfo.Capture(cancellationError).Throw();
        }
        finally { _clearing = false; }
    }

    internal abstract void ClearContent();
    internal abstract void Redraw(bool scaleOnly);
    internal virtual void ReleaseHandlers() { }

    internal void Detach()
    {
        try { ClearCore(); }
        finally
        {
            _removed = true;
            Clearing = null;
            InputPolicyChanged = null;
            ReleaseHandlers();
        }
    }
}
