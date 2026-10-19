using Fizzy.ImageViewer.Hud;
using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Menus;
using Fizzy.ImageViewer.Viewport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

/// <summary>An embeddable image viewer on its host's WPF dispatcher.</summary>
/// <remarks>Create on an STA. Library operations marshal to that dispatcher; inherited WPF members
/// retain WPF thread affinity. Unloading ends interaction but retains content. The owner must await
/// DisposeAsync before stopping the dispatcher. A disposed control cannot be reused.
/// Content is managed by the library and must not be replaced.</remarks>
public sealed partial class ImageViewerControl : UserControl, IViewer
{
    private readonly ViewerRuntime _runtime;
    private readonly ILogger _logger;
    private readonly Layers.ViewerLayers _layers = null!;
    private readonly Grid _surface = null!;
    internal ImageViewport ImageViewport { get; } = null!;
    internal MeasurementOverlay MeasurementOverlay => Layers.Measurements.Overlay;
    internal HudLayer HudLayer { get; } = null!;
    internal ViewerRuntime Runtime => _runtime;

    /// <summary>Creates a viewer on the current STA, ready for use before loading.</summary>
    public ImageViewerControl() : this(NullLogger<ImageViewerControl>.Instance) { }

    /// <summary>Creates a viewer on the current STA. A null logger disables logging.</summary>
    public ImageViewerControl(ILogger<ImageViewerControl>? logger)
        : this(logger ?? NullLogger<ImageViewerControl>.Instance, null, null) { }

    internal ImageViewerControl(ILogger logger, Rendering.ICpuImagePresenter? presenter,
        Action<ViewerInitializationStage>? checkpoint)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("ImageViewerControl requires an STA thread.");
        _logger = logger;
        _runtime = new ViewerRuntime(this, logger, presenter);
        try
        {
            _surface = new Grid { Background = Brushes.Black };
            ImageViewport = new ImageViewport(_surface);
            _layers = new Layers.ViewerLayers(ImageViewport.TransformGroup, _runtime.Lifetime, logger);
            HudLayer = new HudLayer();
            ImageViewport.ScaleChanged += _layers.Collection.UpdateScale;
            _surface.Children.Add(ImageViewport);
            _surface.Children.Add(_layers.Collection.Root);
            _surface.Children.Add(HudLayer);
            Content = _surface;
            _runtime.Initialize(checkpoint);
            Unloaded += OnUnloaded;
        }
        catch (Exception startupError)
        {
            try { _runtime.BeginDisposal().AsTask().GetAwaiter().GetResult(); }
            catch (Exception cleanupError) { throw new AggregateException("Control startup and cleanup failed.", startupError, cleanupError); }
            throw;
        }
    }

    static ImageViewerControl()
    {
        ContentProperty.OverrideMetadata(typeof(ImageViewerControl), new FrameworkPropertyMetadata(null, null,
            (owner, value) =>
            {
                var control = (ImageViewerControl)owner;
                if (control._surface != null && !ReferenceEquals(value, control._surface))
                    throw new InvalidOperationException("ImageViewerControl owns its Content.");
                return value;
            }));
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_runtime.Lifetime.IsStopping) return;
        try { _runtime.Interaction.Cancel(); }
        catch (Exception error) { _runtime.RecordCleanupFailure(error); _logger.LogWarning(error, "Unloaded interaction cleanup failed"); }
        finally { ImageViewport.EndPan(); }
    }

    /// <inheritdoc />
    public IDisposable RegisterMenuItem(IMenuItem menuItem) => InvokeAlive(() => _runtime.Menus.Register(menuItem));

    /// <summary>Stops admission and drains owned work without stopping the host dispatcher.</summary>
    /// <remarks>Repeated calls share the same success or cleanup failure. Independent frame leases
    /// remain valid. Await this method before closing the host dispatcher; never block its UI thread.</remarks>
    public ValueTask DisposeAsync() => _runtime.BeginDisposal();

    /// <inheritdoc />
    public Task WhenStopping => _runtime.Lifetime.WhenStopping;

    internal void ClearNotifications()
    {
        Unloaded -= OnUnloaded;
        if (ImageViewport != null && _layers != null) ImageViewport.ScaleChanged -= _layers.Collection.UpdateScale;
        lock (_runtime.Lifetime.Gate)
        {
            _frameCommitted = null;
            _measurementCompleted = null;
            _measurementRemoved = null;
            _measurementChanged = null;
        }
    }

    private T InvokeAlive<T>(Func<T> action) => _runtime.Lifetime.Invoke(Dispatcher, action);
    private void InvokeAlive(Action action) => InvokeAlive(() => { action(); return true; });
}
