using Fizzy.ImageViewer.Menus;
using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging;
using Fizzy.ImageViewer.Interaction;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

/// <summary>
/// 图像查看器主类，提供图像显示、绘图、测量等功能。
/// <para>
/// 该类在独立的 STA 线程中创建 WPF 窗口，支持从任意线程调用 API。
/// 所有 UI 操作都会自动调度到 UI 线程执行。
/// </para>
/// </summary>
public sealed partial class Viewer : IViewerAPI, IAsyncDisposable
{
    private readonly ViewerRuntime _runtime;
    private readonly ILogger _logger;

    /// <summary>Creates a viewer on its own STA. Use showWindow: false to configure and
    /// subscribe before calling Show. The caller owns disposal even if never shown.</summary>
    public Viewer(ILogger<Viewer> logger, double left = double.NaN, double top = double.NaN, double width = double.NaN, double height = double.NaN,
        bool showWindow = true)
        : this(logger, null, showWindow, left, top, width, height) { }

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
        _runtime = new ViewerRuntime(this, logger, showWindow);
        _runtime.Start(presenter, left, top, width, height, initialize, checkpoint);
    }

    private static void ValidateWindowArgument(double value, string name, bool dimension = false)
    {
        if (!double.IsNaN(value) && (!double.IsFinite(value) || (dimension && value < 0)))
            throw new ArgumentOutOfRangeException(name);
    }

    internal Dispatcher UiDispatcher => _runtime.Window.Dispatcher;
    internal ViewerWindow WindowForTests => _runtime.Window;
    internal InteractionCoordinator Interaction => _runtime.Interaction;
    internal MeasurementContext MeasurementContext => _runtime.Measurements;

    public void RegisterMenu(IMenuItem menuItem)
    {
        InvokeAlive(() => _runtime.Menus.Register(menuItem));
    }
}
