using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Managers;
using Fizzy.ImageViewer.MeasureMethods;
using Fizzy.ImageViewer.MenuItems;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private void InitializeWindow(out Thread thread, out ViewerWindow window, double left, double top, double width, double height)
    {
        var tcs = new TaskCompletionSource<ViewerWindow>();

        thread = new Thread(() =>
        {
            try
            {
                var win = new ViewerWindow("Fizzy ImageViewer", _lifetime);
                _window = win;
                win.Closed += OnWindowClosed;

                // 在 UI 线程创建 Managers
                var measureMgr = new MeasureManager(win.Layer1, AcquireCurrentFrameForMeasurement, _logger);

                var editMgr = new EditManager(win.Layer1, measureMgr.Context);
                var interaction = new InteractionCoordinator(win.Layer0, win.Layer1, editMgr, measureMgr, win.Layers);
                var menuMgr = new MenuManager(win)
                {
                    CheckHasSelection = () => measureMgr.HasSelection,
                    CheckHasSelectedShape = () => win.Layer1.SelectedShape != null,
                    GetSelectedShape = () => win.Layer1.SelectedShape
                };

                // 内置功能注册需要访问测量和交互管理器。
                _measureManager = measureMgr;
                _interaction = interaction;
                RegisterBuiltInFeatures(win, measureMgr, menuMgr);

                // 发布初始化完成的管理器，随后才通知构造线程。
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
    private void RegisterBuiltInFeatures(ViewerWindow win, MeasureManager measureMgr, MenuManager menuMgr)
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
                _interaction?.StartEditing(shape);
        }));
        menuMgr.Register(new MenuItem("Delete", win.Layer1.DeleteSelected, Enums.MenuItemType.SelectionAction));
        menuMgr.Register(SeparatorMenuItem.Instance);

        // 测量工具菜单
        menuMgr.RegisterMeasureTools(() => measureMgr.RegisteredMethods.Select(entry =>
            (IMenuItem)new MenuItem(entry.DisplayName, () =>
            {
                if (measureMgr.HasMethod(entry.Id) && win.Layers.Measurements.IsVisible)
                    _interaction?.StartMeasurement(entry.Id);
            }, Enums.MenuItemType.MeasureTool)).ToArray());

        menuMgr.Register(new MenuItem("Cancel Measurement", () => _interaction?.Cancel(), Enums.MenuItemType.ContextAction));
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
}
