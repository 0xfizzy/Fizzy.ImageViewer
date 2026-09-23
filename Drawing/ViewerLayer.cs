using System.Windows;
using System.Windows.Controls;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Shared visibility, input and lifetime of a business layer. Operations dispatch to the viewer STA.</summary>
public abstract class ViewerLayer
{
    internal ViewerLayers Owner { get; }
    internal Grid Root { get; } = new() { Background = null };
    private bool _visible = true, _hitTest;
    private int _zIndex;
    private bool _removed, _clearing;
    internal event Action? Clearing;
    internal event Action? InputPolicyChanged;
    public string Name { get; }
    internal bool IsBuiltIn { get; }

    internal ViewerLayer(ViewerLayers owner, string name, int zIndex, bool builtIn, bool hitTest)
    {
        Owner = owner;
        Name = name;
        _zIndex = zIndex;
        IsBuiltIn = builtIn;
        _hitTest = hitTest;
        Panel.SetZIndex(Root, zIndex);
        ApplyHitTest();
    }

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
    public void Clear() => Owner.Invoke(() => { EnsureAlive(); ClearCore(); });

    internal void ClearCore()
    {
        if (_clearing) return;
        _clearing = true;
        try { Clearing?.Invoke(); }
        finally
        {
            try { ClearContent(); }
            finally { _clearing = false; }
        }
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
