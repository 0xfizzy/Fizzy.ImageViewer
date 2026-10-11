using Fizzy.ImageViewer.Menus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fizzy.ImageViewer;

/// <summary>
/// Displays images, drawings and measurements in an independently owned WPF window.
/// <para>
/// Creates its WPF window on an independent STA; facade calls marshal to that thread.
/// Borrowed tool contexts retain their own callback-thread restrictions.
/// </para>
/// </summary>
public sealed partial class Viewer : IViewer
{
    private readonly ViewerHost _host;
    private readonly ILogger _logger;

    /// <summary>Creates a viewer on its own STA. Use showWindow: false to configure and
    /// subscribe before calling Show. The caller owns disposal even if never shown.
    /// Omitting logger or passing null disables logging.</summary>
    public Viewer(ILogger<Viewer>? logger = null, double left = double.NaN, double top = double.NaN, double width = double.NaN, double height = double.NaN,
        bool showWindow = true)
        : this(logger ?? NullLogger<Viewer>.Instance, null, showWindow, left, top, width, height) { }

    internal Viewer(ILogger<Viewer> logger, Rendering.ICpuImagePresenter? presenter, bool showWindow,
        double left = double.NaN, double top = double.NaN, double width = double.NaN, double height = double.NaN,
        Action<Viewer>? initialize = null, Action<ViewerInitializationStage>? checkpoint = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ValidateWindowArgument(left, nameof(left));
        ValidateWindowArgument(top, nameof(top));
        ValidateWindowArgument(width, nameof(width), dimension: true);
        ValidateWindowArgument(height, nameof(height), dimension: true);
        _logger = logger;
        _host = new ViewerHost(this, logger, showWindow);
        _host.Start(presenter, left, top, width, height, initialize, checkpoint);
    }

    private static void ValidateWindowArgument(double value, string name, bool dimension = false)
    {
        if (!double.IsNaN(value) && (!double.IsFinite(value) || (dimension && value < 0)))
            throw new ArgumentOutOfRangeException(name);
    }

    internal ViewerHost Host => _host;

    /// <summary>Registers a menu item until its handle is disposed or the viewer closes.
    /// Handle disposal marshals to the viewer STA and is idempotent after closure.</summary>
    public IDisposable RegisterMenuItem(IMenuItem menuItem)
        => InvokeAlive(() => _host.Menus.Register(menuItem));
}
