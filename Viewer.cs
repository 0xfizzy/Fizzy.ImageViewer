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
public partial class Viewer : IViewerAPI
{
    private readonly Thread _windowThread;
    protected ViewerWindow _window;
    private readonly ILogger _logger;

    // === Managers ===
    // 使用 volatile 保证跨线程可见性，因为这些字段在 UI 线程初始化，但可能在其他线程读取
    private volatile MenuManager? _menuManager;
    private volatile MeasureManager? _measureManager;
    
    /// <summary>
    /// 用于同步 Manager 初始化完成的信号。
    /// </summary>
    private readonly ManualResetEventSlim _managersInitialized = new(false);

    /// <summary>
    /// 窗口关闭后触发的事件（包括用户点击关闭按钮）。
    /// </summary>
    public event EventHandler? Closed;

    public Viewer(ILogger<Viewer> logger, double left = double.NaN, double top = double.NaN, double width = double.NaN, double height = double.NaN)
    {
        InitializeWindow(out _windowThread, out _window, left, top, width, height);
        _logger = logger;
    }

    private void InitializeWindow(out Thread thread, out ViewerWindow window, double left, double top, double width, double height)
    {
        var tcs = new TaskCompletionSource<ViewerWindow>();

        thread = new Thread(() =>
        {
            try
            {
                var win = new ViewerWindow("Fizzy ImageViewer");
                win.Closed += (s, e) =>
                {
                    Closed?.Invoke(this, EventArgs.Empty);
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                };

                // 在 UI 线程创建 Managers
                var measureMgr = new MeasureManager(win.Layer0, win.Layer1);
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
                RegisterBuiltInFeatures(win, measureMgr, editMgr, menuMgr);

                // 使用 volatile 写入确保其他线程可见
                _measureManager = measureMgr;
                _menuManager = menuMgr;

                // 发出初始化完成信号
                _managersInitialized.Set();

                // Set window position before showing (if provided)
                if (!double.IsNaN(left)) win.Left = left;
                if (!double.IsNaN(top)) win.Top = top;
                if (!double.IsNaN(width)) win.Width = width;
                if (!double.IsNaN(height)) win.Height = height;

                tcs.SetResult(win);
                win.Show();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _managersInitialized.Set(); // 即使失败也要释放等待的线程
                tcs.TrySetException(ex);
            }
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
        menuMgr.Register(new MenuItem("Clear All Shapes", () => { measureMgr.Cancel(); win.Layer1.Clear(); }));
        menuMgr.Register(new SaveImageMenuItem(win.Layer0));
        menuMgr.Register(SeparatorMenuItem.Instance);

        // 初始化像素信息叠加层
        InitializePixelInfoOverlay(win, menuMgr);
        
        // 连接菜单打开/关闭事件到帧冻结/解冻
        menuMgr.OnMenuOpening = Freeze;
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
        // 等待 Managers 初始化完成，避免空引用
        _managersInitialized.Wait();
        _window?.Dispatcher.Invoke(() => _menuManager?.Register(menuItem));
    }

    public void RegisterMeasureMethod(IMeasureMethod method)
    {
        // 等待 Managers 初始化完成，避免空引用
        _managersInitialized.Wait();
        _window?.Dispatcher.Invoke(() => _measureManager?.RegisterMethod(method));
    }

    public void StartMeasure(string methodName)
    {
        _managersInitialized.Wait();
        _window?.Dispatcher.Invoke(() => _measureManager?.Start(methodName));
    }

    public void CancelMeasure()
    {
        _managersInitialized.Wait();
        _window?.Dispatcher.Invoke(() => _measureManager?.Cancel());
    }

    public void FitImageToContainer()
    {
        _window?.Dispatcher.Invoke(() => _window.Layer0.FitImageToContainer());
    }

    public virtual void Dispose()
    {
        try
        {
            _window?.Dispatcher.Invoke(() =>
            {
                _window.CanUserClose = true;
                _window.Close();
            });
        }
        catch (Exception)
        {

        }
        _managersInitialized.Dispose();
        GC.SuppressFinalize(this);
    }
}
