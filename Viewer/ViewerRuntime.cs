using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements.BuiltIn;
using Fizzy.ImageViewer.Menus;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Fizzy.ImageViewer.PixelInfo;

namespace Fizzy.ImageViewer;

internal enum ViewerInitializationStage { WindowCreated, PipelineCreated, MeasurementsCreated }

/// <summary>Owns the viewer STA, component composition and shutdown.</summary>
internal sealed class ViewerRuntime(Viewer owner, ILogger logger, bool showWindow)
{
    private readonly Viewer _owner = owner;
    private readonly ILogger _logger = logger;
    private readonly bool _showWindow = showWindow;
    private Thread _windowThread = null!;
    private ViewerWindow _window = null!;
    private Frames.FramePipeline _pipeline = null!;
    private Rendering.FramePresentation _presentation = null!;
    private MeasurementToolRegistry _tools = null!;
    private MeasurementContext _context = null!;
    private InteractionCoordinator _interaction = null!;
    private MenuManager _menuManager = null!;
    private Imaging.Queries.PixelQueryScheduler _queryScheduler = null!;
    private Snapshots.MenuSnapshotSession _menuSession = null!;
    private readonly Snapshots.SnapshotCapture _snapshotCapture = new();
    private HudTextCollection _hud = null!;
    private PixelInfoOverlay? _pixelInfoOverlay;
    private readonly ViewerLifetime _lifetime = new();
    private readonly TaskCompletionSource _windowStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeRequested;

    internal ViewerLifetime Lifetime => _lifetime;
    internal ViewerWindow Window => _window;
    internal Frames.FramePipeline Pipeline => _pipeline;
    internal MeasurementToolRegistry Tools => _tools;
    internal MeasurementContext Measurements => _context;
    internal InteractionCoordinator Interaction => _interaction;
    internal MenuManager Menus => _menuManager;
    internal Imaging.Queries.PixelQueryScheduler Queries => _queryScheduler;
    internal Snapshots.MenuSnapshotSession MenuSession => _menuSession;
    internal Snapshots.SnapshotCapture Snapshots => _snapshotCapture;
    internal HudTextCollection Hud => _hud;
    private Frames.FrameLease? TryAcquireCurrentFrame() => _pipeline.TryAcquireCurrentFrame();
    private void Unfreeze() => _menuSession.Close();
    internal void FreezeMenuRegion() => _menuSession.Open(descriptor =>
        _interaction?.SelectedMeasurement is { IsComplete: true, Geometry.Kind: ShapeType.Rectangle } item
            ? item.Geometry.ToRegion(descriptor) : null);
    internal void Start(Rendering.ICpuImagePresenter? presenter,
        double left, double top, double width, double height, Action<Viewer>? initialize, Action<ViewerInitializationStage>? checkpoint)
    {
        var tcs = new TaskCompletionSource<ViewerWindow>();

        _windowThread = new Thread(() =>
        {
            ViewerWindow? win = null;
            Exception? failure = null;
            try
            {
                win = new ViewerWindow("Fizzy ImageViewer", _lifetime);
                _window = win;
                checkpoint?.Invoke(ViewerInitializationStage.WindowCreated);
                _presentation = new Rendering.FramePresentation(win.Dispatcher, win.ImageLayer,
                    presenter ?? new Rendering.WriteableBitmapPresenter(), _logger);
                _pipeline = new Frames.FramePipeline(_lifetime, win.Dispatcher, _presentation, _logger, _owner.NotifyFrameCommitted);
                checkpoint?.Invoke(ViewerInitializationStage.PipelineCreated);
                _hud = new HudTextCollection(win.HudLayer, _lifetime);
                _menuSession = new Snapshots.MenuSnapshotSession(_pipeline, _lifetime, win.Dispatcher, _logger);
                win.Closed += OnWindowClosed;

                _queryScheduler = new(TryAcquireCurrentFrame, _logger, new Imaging.Queries.DispatcherQueryRuntime(win.Dispatcher));
                _context = new MeasurementContext(win.MeasurementOverlay, TryAcquireCurrentFrame, _queryScheduler, _logger);
                _tools = new MeasurementToolRegistry();
                _context.ItemCompleted += _owner.NotifyMeasurementCompleted;
                _context.ItemRemoved += _owner.NotifyMeasurementRemoved;

                checkpoint?.Invoke(ViewerInitializationStage.MeasurementsCreated);
                var editMgr = new EditManager(win.MeasurementOverlay, _context.Find);
                _interaction = new InteractionCoordinator(new ViewerInputBinding(win.ImageLayer, win.MeasurementOverlay),
                    win.MeasurementOverlay, editMgr, _tools, _context, win.Layers);
                _menuManager = new MenuManager(win);
                RegisterBuiltInFeatures(win, _menuManager);

                // Set window position before showing (if provided)
                if (!double.IsNaN(left)) win.Left = left;
                if (!double.IsNaN(top)) win.Top = top;
                if (!double.IsNaN(width)) win.Width = width;
                if (!double.IsNaN(height)) win.Height = height;

                initialize?.Invoke(_owner);
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

        _windowThread.SetApartmentState(ApartmentState.STA);
        _windowThread.IsBackground = true;
        _windowThread.Start();

        try { tcs.Task.GetAwaiter().GetResult(); }
        catch
        {
            _windowThread.Join();
            // Cancellation can still be releasing frame/query leases off the STA.
            try { _disposeCompletion.Task.GetAwaiter().GetResult(); }
            catch (Exception cleanupError) { _logger.LogWarning(cleanupError, "Startup cleanup failed"); }
            throw;
        }
    }

    /// <summary>
    /// 注册内置的测量方法和菜单项。
    /// </summary>
    private void RegisterBuiltInFeatures(ViewerWindow win, MenuManager menuMgr)
    {
        // 注册测量方法
        _tools.RegisterTool(new LineTool());
        _tools.RegisterTool(new PointTool());
        _tools.RegisterTool(new RectTool());
        _tools.RegisterTool(new LineStrengthTool());

        menuMgr.Register(CreateInteractionMenu);
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
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            CleanupOnWindowClosed();
            _ = BeginDisposal();
            _owner.NotifyClosed(e);
        }
        finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
    }

    private void CleanupOnWindowClosed()
    {
        if (_closed) return;
        _lifetime.BeginDisposal();
        _closed = true;
        Cleanup(() => _pipeline?.StopOnUiThread());
        Cleanup(() => _menuSession?.Dispose());

        Cleanup(() => _interaction?.Dispose());
        Cleanup(() => _pixelInfoOverlay?.Disable());
        Cleanup(() => _queryScheduler?.Dispose());
        Cleanup(() => _context?.Shutdown());
        Cleanup(() => _tools?.Clear());
        Cleanup(() => _window?.Layers.Close());
        Cleanup(() => _hud?.Dispose());
        Cleanup(() => _presentation?.Dispose());
        _owner.ClearNotifications();
    }

    private void Cleanup(Action action)
    {
        try { action(); } catch (Exception ex) { _logger.LogWarning(ex, "Viewer cleanup failed"); }
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            if (!_closed && _window != null && !_window.Dispatcher.HasShutdownStarted)
            {
                try
                {
                    await _window.Dispatcher.InvokeAsync(() => _window.CloseProgrammatically()).Task.ConfigureAwait(false);
                }
                catch (TaskCanceledException) { }
            }
            await Task.WhenAll(_pipeline?.Completion ?? Task.CompletedTask, _queryScheduler?.Completion ?? Task.CompletedTask, _windowStopped.Task).ConfigureAwait(false);
            // The stop signal is set in the STA's finally block, just before it returns.
            // Join off-thread so DisposeAsync also guarantees actual thread termination.
            await Task.Run(_windowThread.Join).ConfigureAwait(false);
            _lifetime.Complete();
            if (_window?.ClosingError is { } error) _disposeCompletion.TrySetException(error);
            else _disposeCompletion.TrySetResult();
        }
        catch (Exception ex) { _disposeCompletion.TrySetException(ex); }
    }

    // Window closure and explicit disposal share one completion task.
    internal ValueTask BeginDisposal()
    {
        _lifetime.BeginDisposal();
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0) _ = CompleteDisposalAsync();
        return new ValueTask(_disposeCompletion.Task);
    }

    // Capture interaction once per opening. Menu rendering knows no measurement policy.
    private IEnumerable<IMenuItem> CreateInteractionMenu()
    {
        var interaction = _interaction;
        var measuring = interaction.Mode == InteractionMode.Measuring;
        var selected = interaction.SelectedShape;
        if (measuring)
        {
            yield return new MenuItem("Cancel Measurement", interaction.Cancel);
            yield break;
        }
        if (selected != null)
        {
            yield return new MenuItem("Edit", () => interaction.StartEditing(selected));
            yield return new MenuItem("Delete", () => interaction.Delete(selected));
            yield return SeparatorMenuItem.Instance;
        }
        foreach (var tool in _tools.RegisteredTools)
            yield return new MenuItem(tool.DisplayName, () =>
            {
                if (_tools.HasTool(tool.Id) && _window.Layers.Measurements.IsVisible)
                    interaction.StartMeasurement(tool.Id);
            });
    }
    private void InitializePixelInfoOverlay(ViewerWindow win, Menus.MenuManager menuMgr)
    {
        _pixelInfoOverlay = new PixelInfoOverlay(win.ImageLayer, win.HudLayer, _queryScheduler);
        _pixelInfoOverlay.Enable();
        menuMgr.Register(new Menus.CheckableMenuItem("Pixel Info", () => _pixelInfoOverlay.IsEnabled,
            () => { if (_pixelInfoOverlay.IsEnabled) _pixelInfoOverlay.Disable(); else _pixelInfoOverlay.Enable(); }));
    }
}
