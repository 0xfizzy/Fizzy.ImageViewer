using Microsoft.Extensions.Logging;
using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Managers;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

/// <summary>
/// 图像查看器主类，提供图像显示、绘图、测量等功能。
/// <para>
/// 该类在独立的 STA 线程中创建 WPF 窗口，支持从任意线程调用 API。
/// 所有 UI 操作都会自动调度到 UI 线程执行。
/// </para>
/// </summary>
public partial class Viewer : IViewerAPI, IAsyncDisposable
{
    private readonly Thread _windowThread;
    private ViewerWindow _window;
    private readonly ILogger _logger;

    // === Managers ===
    // 使用 volatile 保证跨线程可见性，因为这些字段在 UI 线程初始化，但可能在其他线程读取
    private volatile MenuManager? _menuManager;
    private volatile MeasureManager? _measureManager;
    private volatile InteractionCoordinator? _interaction;

    public Viewer(ILogger<Viewer> logger, double left = double.NaN, double top = double.NaN, double width = double.NaN, double height = double.NaN)
        : this(logger, null, true, left, top, width, height) { }

    internal Viewer(ILogger<Viewer> logger, Rendering.IImagePresenter? presenter, bool showWindow,
        double left = double.NaN, double top = double.NaN, double width = double.NaN, double height = double.NaN)
    {
        _logger = logger;
        _showWindow = showWindow;
        InitializeWindow(presenter, out _windowThread, out _window, left, top, width, height);
    }

    private readonly bool _showWindow;

    internal Dispatcher UiDispatcher => _window.Dispatcher;
    internal ViewerWindow WindowForTests => _window;
    internal InteractionCoordinator Interaction => _interaction!;
    internal MeasureContext MeasurementContext => _measureManager!.Context;

    public void RegisterMenu(IMenuItem menuItem)
    {
        InvokeAlive(() => _menuManager!.Register(menuItem));
    }
}
