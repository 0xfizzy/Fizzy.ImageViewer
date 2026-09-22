using Microsoft.Extensions.Logging;
using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Managers;
using Fizzy.ImageViewer.MeasureMethods;
using Fizzy.ImageViewer.MenuItems;
using System.Threading;
using System.Windows;
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
    protected ViewerWindow _window;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _windowStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // === Managers ===
    // 使用 volatile 保证跨线程可见性，因为这些字段在 UI 线程初始化，但可能在其他线程读取
    private volatile MenuManager? _menuManager;
    private volatile MeasureManager? _measureManager;
    
    /// <summary>
    /// 窗口关闭后触发的事件（包括用户点击关闭按钮）。
    /// </summary>
    public event EventHandler? Closed;

    public Viewer(ILogger<Viewer> logger, double left = double.NaN, double top = double.NaN, double width = double.NaN, double height = double.NaN)
        : this(logger, new Rendering.WriteableBitmapPresenter(), true, left, top, width, height) { }

    internal Viewer(ILogger<Viewer> logger, Rendering.IImagePresenter presenter, bool showWindow,
        double left = double.NaN, double top = double.NaN, double width = double.NaN, double height = double.NaN)
    {
        _logger = logger;
        _presenter = presenter;
        _showWindow = showWindow;
        InitializeWindow(out _windowThread, out _window, left, top, width, height);
    }

    private readonly bool _showWindow;
    public Drawing.ViewerLayers Layers => _window.Layers;

    internal Dispatcher UiDispatcher => _window.Dispatcher;
    internal MeasureContext MeasurementContext => _measureManager!.Context;

    private void InitializeWindow(out Thread thread, out ViewerWindow window, double left, double top, double width, double height)
    {
        var tcs = new TaskCompletionSource<ViewerWindow>();

        thread = new Thread(() =>
        {
            try
            {
                var win = new ViewerWindow("Fizzy ImageViewer");
                _window = win;
                win.Closed += (s, e) =>
                {
                    try
                    {
                        StopRendering();
                        foreach (EventHandler handler in Closed?.GetInvocationList() ?? [])
                            try { handler(this, EventArgs.Empty); } catch (Exception ex) { _logger.LogWarning(ex, "Closed handler failed"); }
                    }
                    finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
                };

                // 在 UI 线程创建 Managers
                var measureMgr = new MeasureManager(win.Layer0, win.Layer1, AcquireCurrentFrame, _logger);
                measureMgr.InputSuppressionChanged += win.Layers.SuppressInput;
                win.Layers.CancelMeasurement = measureMgr.Cancel;
                var editMgr = new EditManager(win.Layer0, win.Layer1);
                var menuMgr = new MenuManager(win)
                {
                    CheckHasSelection = () => measureMgr.HasSelection,
                    CheckHasSelectedShape = () => win.Layer1.SelectedShape != null,
                    GetSelectedShape = () => win.Layer1.SelectedShape
                };

                // Connect EditManager to OverlayLayer
                win.Layer1.SetEditManager(editMgr);

                // 注册内置功能（在赋值给字段之前，确保 Managers 完全初始化）
                _measureManager = measureMgr;
                RegisterBuiltInFeatures(win, measureMgr, editMgr, menuMgr);

                // 使用 volatile 写入确保其他线程可见
                _measureManager = measureMgr;
                _menuManager = menuMgr;

                // Set window position before showing (if provided)
                if (!double.IsNaN(left)) win.Left = left;
                if (!double.IsNaN(top)) win.Top = top;
                if (!double.IsNaN(width)) win.Width = width;
                if (!double.IsNaN(height)) win.Height = height;

                if (_showWindow) win.Show();
                tcs.SetResult(win);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally { _windowStopped.TrySetResult(); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        window = tcs.Task.Result;
    }

    /// <summary>
    /// 注册内置的测量方法和菜单项。
    /// </summary>
    private void RegisterBuiltInFeatures(ViewerWindow win, MeasureManager measureMgr, EditManager editMgr, MenuManager menuMgr)
    {
        // 注册测量方法
        measureMgr.RegisterMethod(new LineMeasure());
        measureMgr.RegisterMethod(new PointMeasure());
        measureMgr.RegisterMethod(new RectMeasure());
        measureMgr.RegisterMethod(new LineStrengthMeasure());

        // 菜单注册 - 使用简化的 lambda API
        // Edit menu item (positioned before Delete)
        menuMgr.Register(new MenuItems.EditMenuItem(() =>
        {
            var shape = menuMgr.GetMenuTargetShape();
            if (shape != null)
                win.Layer1.EnterEditMode(shape);
        }));
        menuMgr.Register(new MenuItem("Delete", win.Layer1.DeleteSelected, Enums.MenuItemType.SelectionAction));
        menuMgr.Register(SeparatorMenuItem.Instance);
        
        // 测量工具菜单
        foreach (var method in measureMgr.RegisteredMethods.Keys)
        {
            menuMgr.Register(new MenuItem(method, () => measureMgr.Start(method), Enums.MenuItemType.MeasureTool));
        }
        
        menuMgr.Register(new MenuItem("Cancel Measurement", measureMgr.Cancel, Enums.MenuItemType.ContextAction));
        menuMgr.Register(SeparatorMenuItem.Instance);
        menuMgr.Register(new MenuItem("Clear All Shapes", () => { win.Layers.Clear(); }));
        menuMgr.Register(new SaveImageMenuItem(this, false));
        menuMgr.Register(new SaveImageMenuItem(this, true));
        menuMgr.Register(new SaveImageMenuItem(this, false, true));
        menuMgr.Register(new SaveImageMenuItem(this, true, true));
        menuMgr.Register(SeparatorMenuItem.Instance);

        // 初始化像素信息叠加层
        InitializePixelInfoOverlay(win, menuMgr);
        
        // 连接菜单打开/关闭事件到帧冻结/解冻
        menuMgr.OnMenuOpening = FreezeMenuRegion;
        menuMgr.OnMenuClosing = Unfreeze;
    }

    // === API Implementation ===

    public void Show() => _window?.Dispatcher.Invoke(() =>
    {
        if (_window!.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Show();
        _window.Activate();   // raise above any maximized/foreground window
    });
    public void Close() => _window?.Dispatcher.Invoke(() => _window.Close());

    public void RegisterMenu(IMenuItem menuItem)
    {
        _window?.Dispatcher.Invoke(() => _menuManager?.Register(menuItem));
    }

    public void RegisterMeasureMethod(IMeasureMethod method)
    {
        // 等待 Managers 初始化完成，避免空引用
        _window?.Dispatcher.Invoke(() => _measureManager?.RegisterMethod(method));
    }

    public void StartMeasure(string methodName)
    {
        _window?.Dispatcher.Invoke(() => _measureManager?.Start(methodName));
    }

    public void CancelMeasure()
    {
        _window?.Dispatcher.Invoke(() => _measureManager?.Cancel());
    }

    public void FitImageToContainer()
    {
        _window?.Dispatcher.Invoke(() => _window.Layer0.FitImageToContainer());
    }

    public virtual void Dispose()
    {
        if (_window.Dispatcher.CheckAccess()) { _window.CanUserClose = true; _window.Close(); }
        else DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
