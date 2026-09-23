using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements.Methods;
using Fizzy.ImageViewer.Menus;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private void InitializeWindow(Rendering.ICpuImagePresenter? presenter, out Thread thread, out ViewerWindow window,
        double left, double top, double width, double height, Action<Viewer>? initialize)
    {
        var tcs = new TaskCompletionSource<ViewerWindow>();

        thread = new Thread(() =>
        {
            ViewerWindow? win = null;
            Exception? failure = null;
            try
            {
                win = new ViewerWindow("Fizzy ImageViewer", _lifetime);
                _window = win;
                _presentation = new Rendering.FramePresentation(win.Dispatcher, win.ImageLayer,
                    presenter ?? new Rendering.WriteableBitmapPresenter(), _logger);
                _pipeline = new Frames.FramePipeline(_lifetime, win.Dispatcher, _presentation, _logger, NotifyFrameCommitted);
                _menuSession = new Snapshots.MenuSnapshotSession(_pipeline, _lifetime, win.Dispatcher, _logger);
                win.Closed += OnWindowClosed;

                // 在 UI 线程创建 Managers
                _queryScheduler = new(AcquireCurrentFrameForMeasurement, _logger, new Imaging.Queries.DispatcherQueryRuntime(win.Dispatcher));
                var measureMgr = _measureManager = new MeasureManager(win.MeasurementOverlay, AcquireCurrentFrameForMeasurement, _queryScheduler, _logger);
                measureMgr.Context.ItemCompleted += item => NotifyMeasurement(MeasurementCompleted, item);
                measureMgr.Context.ItemRemoved += item => NotifyMeasurement(MeasurementRemoved, item);

                var editMgr = new EditManager(win.MeasurementOverlay, measureMgr.Context);
                var interaction = new InteractionCoordinator(win.ImageLayer, win.MeasurementOverlay, editMgr, measureMgr, win.Layers);
                var menuMgr = new MenuManager(win)
                {
                    IsMeasuring = () => measureMgr.IsMeasuring,
                    CheckHasSelectedShape = () => interaction.SelectedShape != null,
                    GetSelectedShape = () => interaction.SelectedShape
                };

                // 内置功能注册需要访问测量和交互管理器。
                _interaction = interaction;
                RegisterBuiltInFeatures(win, measureMgr, menuMgr);

                // 发布初始化完成的管理器，随后才通知构造线程。
                _menuManager = menuMgr;

                // Set window position before showing (if provided)
                if (!double.IsNaN(left)) win.Left = left;
                if (!double.IsNaN(top)) win.Top = top;
                if (!double.IsNaN(width)) win.Width = width;
                if (!double.IsNaN(height)) win.Height = height;

                initialize?.Invoke(this);
                if (_showWindow) win.Show();
                tcs.SetResult(win);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                failure = ex;
                if (tcs.Task.IsCompletedSuccessfully) _logger.LogError(ex, "Viewer dispatcher failed");
            }
            finally
            {
                // Partial startup and normal closure share the same resource owners.
                if (win != null) win.Closed -= OnWindowClosed;
                CleanupOnWindowClosed();
                if (_presentation == null) Cleanup(() => presenter?.Dispose());
                if (win != null) Cleanup(win.CloseProgrammatically);
                Cleanup(Dispatcher.CurrentDispatcher.InvokeShutdown);
                _windowStopped.TrySetResult();
                _ = BeginDisposal();
                if (failure != null) tcs.TrySetException(failure);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        try { window = tcs.Task.GetAwaiter().GetResult(); }
        catch
        {
            thread.Join();
            // Cancellation can still be releasing frame/query leases off the STA.
            try { _disposeCompletion.Task.GetAwaiter().GetResult(); }
            catch (Exception cleanupError) { _logger.LogWarning(cleanupError, "Startup cleanup failed"); }
            throw;
        }
    }

    /// <summary>
    /// 注册内置的测量方法和菜单项。
    /// </summary>
    private void RegisterBuiltInFeatures(ViewerWindow win, MeasureManager measureMgr, MenuManager menuMgr)
    {
        // 注册测量方法
        measureMgr.RegisterTool(new LineTool(measureMgr.Context));
        measureMgr.RegisterTool(new PointTool(measureMgr.Context));
        measureMgr.RegisterTool(new RectTool(measureMgr.Context));
        measureMgr.RegisterTool(new LineStrengthTool(measureMgr.Context));

        // 菜单注册 - 使用简化的 lambda API
        // Edit menu item (positioned before Delete)
        menuMgr.Register(new MenuItem("Edit", () =>
        {
            var shape = menuMgr.GetMenuTargetShape();
            if (shape != null)
                _interaction?.StartEditing(shape);
        }, Enums.MenuItemType.SelectionAction));
        menuMgr.Register(new MenuItem("Delete", _interaction!.DeleteSelected, Enums.MenuItemType.SelectionAction));
        menuMgr.Register(SeparatorMenuItem.Instance);

        // 测量工具菜单
        menuMgr.RegisterMeasureTools(() => measureMgr.RegisteredTools.Select(entry =>
            (IMenuItem)new MenuItem(entry.DisplayName, () =>
            {
                if (measureMgr.HasTool(entry.Id) && win.Layers.Measurements.IsVisible)
                    _interaction?.StartMeasurement(entry.Id);
            }, Enums.MenuItemType.MeasureTool)).ToArray());

        menuMgr.Register(new MenuItem("Cancel Measurement", () => _interaction?.Cancel(), Enums.MenuItemType.ContextAction));
        menuMgr.Register(SeparatorMenuItem.Instance);
        menuMgr.Register(new MenuItem("Clear All Shapes", () => { win.Layers.Clear(); }));
        menuMgr.Register(new SaveImageMenuItem(_menuSession, _snapshotCapture, false));
        menuMgr.Register(new SaveImageMenuItem(_menuSession, _snapshotCapture, true));
        menuMgr.Register(new SaveImageMenuItem(_menuSession, _snapshotCapture, false, true));
        menuMgr.Register(new SaveImageMenuItem(_menuSession, _snapshotCapture, true, true));
        menuMgr.Register(SeparatorMenuItem.Instance);

        // 初始化像素信息叠加层
        InitializePixelInfoOverlay(win, menuMgr);

        // 连接菜单打开/关闭事件到帧冻结/解冻
        menuMgr.OnMenuOpening = FreezeMenuRegion;
        menuMgr.OnMenuClosing = Unfreeze;
    }
}
