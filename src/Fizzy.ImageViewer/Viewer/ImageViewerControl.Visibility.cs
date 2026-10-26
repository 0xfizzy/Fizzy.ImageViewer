using System.Windows;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    private Window? _visibilityHost;

    /// <summary>Controls this element's own visibility on its WPF dispatcher; false uses Hidden.</summary>
    /// <remarks>Use the inherited Visibility property for XAML bindings or Collapsed. Ancestors may
    /// still hide a locally visible control; IsVisible reports effective WPF visibility.</remarks>
    public bool Visible
    {
        get { Dispatcher.VerifyAccess(); return Visibility == Visibility.Visible; }
        set { Dispatcher.VerifyAccess(); _runtime.Lifetime.ThrowIfStopping(); SetCurrentValue(VisibilityProperty, value ? Visibility.Visible : Visibility.Hidden); }
    }

    /// <summary>Whether the current containing window is minimized; false when no window contains this control.</summary>
    /// <remarks>Read on the control's WPF dispatcher. This state is independent of IsVisible.</remarks>
    public bool IsMinimized
    {
        get { Dispatcher.VerifyAccess(); return Window.GetWindow(this)?.WindowState == WindowState.Minimized; }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_runtime.Lifetime.IsStopping) return;
        DetachVisibilityHost();
        _visibilityHost = Window.GetWindow(this);
        if (_visibilityHost != null) _visibilityHost.StateChanged += OnHostStateChanged;
        UpdateDisplayVisibility();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdateDisplayVisibility();
    private void OnHostStateChanged(object? sender, EventArgs args) => UpdateDisplayVisibility();

    private void UpdateDisplayVisibility()
    {
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        if (!_runtime.Lifetime.IsStopping)
            _runtime.Pipeline.SetDisplayEnabled(IsLoaded && IsVisible && !IsMinimized);
    }

    private void DetachVisibilityHost()
    {
        if (_visibilityHost != null) _visibilityHost.StateChanged -= OnHostStateChanged;
        _visibilityHost = null;
    }
}
