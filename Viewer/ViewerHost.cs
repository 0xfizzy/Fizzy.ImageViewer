using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Measurements.Editing;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements.BuiltIn;
using Fizzy.ImageViewer.Menus;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Fizzy.ImageViewer.Hud;

namespace Fizzy.ImageViewer;

/// <summary>Owns the viewer STA, component composition and shutdown.</summary>
internal sealed class ViewerHost(Viewer owner, ILogger logger, bool showWindow)
{
    private readonly Viewer _owner = owner;
    private readonly ILogger _logger = logger;
    private readonly bool _showWindow = showWindow;
    private Thread _windowThread = null!;
    private ViewerWindow _window = null!;
    private Rendering.FramePipeline _pipeline = null!;
    private Rendering.FramePresentation _presentation = null!;
    private MeasurementToolRegistry _tools = null!;
    private MeasurementCollection _measurements = null!;
    private InteractionCoordinator _interaction = null!;
    private MenuManager _menuManager = null!;
    private ViewerMenuController? _menuController;
    private Imaging.Queries.PixelQueryScheduler _queryScheduler = null!;
    private Menus.MenuSnapshotSession _menuSession = null!;
    private readonly Snapshots.SnapshotCapture _snapshotCapture = new();
    private HudTextCollection _hud = null!;
    private PixelInfoController? _pixelInfo;
    private readonly ViewerLifetime _lifetime = new();
    private readonly TaskCompletionSource _windowStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeRequested;

    internal PixelInfoController PixelInfo => _pixelInfo!;
    internal ViewerLifetime Lifetime => _lifetime;
    internal ViewerWindow Window => _window;
    internal Rendering.FramePipeline Pipeline => _pipeline;
    internal MeasurementToolRegistry Tools => _tools;
    internal MeasurementCollection Measurements => _measurements;
    internal InteractionCoordinator Interaction => _interaction;
    internal MenuManager Menus => _menuManager;
    internal Imaging.Queries.PixelQueryScheduler Queries => _queryScheduler;
    internal Menus.MenuSnapshotSession MenuSession => _menuSession;
    internal Snapshots.SnapshotCapture Snapshots => _snapshotCapture;
    internal HudTextCollection Hud => _hud;
    private Frames.FrameLease? TryAcquireCurrentFrame() => _pipeline.TryAcquireCurrentFrame();
    internal ViewerMenuController MenuController => _menuController!;
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
                _presentation = new Rendering.FramePresentation(win.Dispatcher, win.ImageViewport,
                    presenter ?? new Rendering.WriteableBitmapPresenter(), _logger);
                _pipeline = new Rendering.FramePipeline(_lifetime, win.Dispatcher, _presentation, _logger, NotifyFrameCommitted);
                checkpoint?.Invoke(ViewerInitializationStage.PipelineCreated);
                _hud = new HudTextCollection(win.HudLayer, _lifetime);
                _menuSession = new Menus.MenuSnapshotSession(_pipeline, _lifetime, win.Dispatcher, _logger);
                win.Closed += OnWindowClosed;

                _queryScheduler = new(TryAcquireCurrentFrame, _logger, new Imaging.Queries.DispatcherQueryRuntime(win.Dispatcher));
                _measurements = new MeasurementCollection(win.Layers.Measurements, _lifetime, win.Dispatcher, TryAcquireCurrentFrame, _queryScheduler, _logger);
                _tools = new MeasurementToolRegistry();
                _measurements.ItemCompleted += _owner.NotifyMeasurementCompleted;
                _measurements.ItemRemoved += _owner.NotifyMeasurementRemoved;
                _measurements.ItemChanged += _owner.NotifyMeasurementChanged;

                checkpoint?.Invoke(ViewerInitializationStage.MeasurementsCreated);
                var editor = new MeasurementEditController(win.MeasurementOverlay);
                _interaction = new InteractionCoordinator(new ViewerInputBinding(win.ImageViewport, win.MeasurementOverlay),
                    win.MeasurementOverlay, editor, _tools, _measurements, win.Layers);
                _tools.RegisterTool(new LengthTool());
                _tools.RegisterTool(new PointTool());
                _tools.RegisterTool(new RectangleRoiTool());
                _tools.RegisterTool(new LineProfileTool());
                _pixelInfo = new PixelInfoController(win.ImageViewport, win.HudLayer, _queryScheduler);
                _pixelInfo.Enable();
                _menuManager = new MenuManager(win, _lifetime, _logger);
                _menuController = new ViewerMenuController(_menuManager, _interaction, _tools, win.Layers,
                    _menuSession, _snapshotCapture, _pixelInfo);
                checkpoint?.Invoke(ViewerInitializationStage.MenusCreated);

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

    private void NotifyFrameCommitted(Frames.FrameLease frame, Frames.FrameSubmissionOptions? options)
    {
        try
        {
            using var borrowed = frame.Acquire();
            options?.OnCommitted?.Invoke(borrowed);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Frame notification failed"); }
        _owner.NotifyFrameCommitted(frame.Info);
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
        Cleanup(() => _menuController?.Dispose());
        Cleanup(() => _menuManager?.Dispose());
        Cleanup(() => _pipeline?.StopOnUiThread());
        Cleanup(() => _menuSession?.Dispose());

        Cleanup(() => _interaction?.Dispose());
        Cleanup(() => _pixelInfo?.Disable());
        Cleanup(() => _queryScheduler?.Dispose());
        Cleanup(() => _measurements?.Shutdown());
        Cleanup(() => _tools?.Clear());
        Cleanup(() => _window?.Layers.Collection.Close());
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

}
