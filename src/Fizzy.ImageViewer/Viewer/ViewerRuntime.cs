using Fizzy.ImageViewer.Measurements.Editing;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements.BuiltIn;
using Fizzy.ImageViewer.Menus;
using Microsoft.Extensions.Logging;
using Fizzy.ImageViewer.Hud;

namespace Fizzy.ImageViewer;

/// <summary>Owns one control's components and shutdown; borrows its UI dispatcher.</summary>
internal sealed class ViewerRuntime(ImageViewerControl view, ILogger logger, Rendering.ICpuImagePresenter? presenter)
{
    private readonly ImageViewerControl _view = view;
    private readonly ILogger _logger = logger;
    private Rendering.ICpuImagePresenter? _unattachedPresenter = presenter;
    private Rendering.FramePipeline _pipeline = null!;
    private Rendering.FramePresentation _presentation = null!;
    private MeasurementToolRegistry _tools = null!;
    private MeasurementCollection _measurements = null!;
    private MeasurementRuntime _measurementRuntime = null!;
    private MeasurementInteractionCoordinator _interaction = null!;
    private MenuManager _menuManager = null!;
    private ViewerMenuController? _menuController;
    private Imaging.Queries.PixelQueryScheduler _queryScheduler = null!;
    private Menus.MenuSnapshotSession _menuSession = null!;
    private readonly Snapshots.SnapshotCapture _snapshotCapture = new();
    private HudTextCollection _hud = null!;
    private PixelInfoController? _pixelInfo;
    private readonly ViewerLifetime _lifetime = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _cleanupErrors = new();
    private volatile bool _closed;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeRequested;

    internal PixelInfoController PixelInfo => _pixelInfo!;
    internal ViewerLifetime Lifetime => _lifetime;
    internal Rendering.FramePipeline Pipeline => _pipeline;
    internal MeasurementToolRegistry Tools => _tools;
    internal MeasurementCollection Measurements => _measurements;
    internal MeasurementRuntime MeasurementRuntime => _measurementRuntime;
    internal MeasurementInteractionCoordinator Interaction => _interaction;
    internal MenuManager Menus => _menuManager;
    internal Imaging.Queries.PixelQueryScheduler Queries => _queryScheduler;
    internal Menus.MenuSnapshotSession MenuSession => _menuSession;
    internal Snapshots.SnapshotCapture Snapshots => _snapshotCapture;
    internal HudTextCollection Hud => _hud;
    private Frames.FrameLease? TryAcquireCurrentFrame() => _pipeline.TryAcquireCurrentFrame();
    internal ViewerMenuController MenuController => _menuController!;
    internal void Initialize(Action<ViewerInitializationStage>? checkpoint)
    {
        _view.Dispatcher.ShutdownStarted += OnDispatcherShutdown;
        checkpoint?.Invoke(ViewerInitializationStage.ViewCreated);
        _presentation = new Rendering.FramePresentation(_view.Dispatcher, _view.ImageViewport,
            _unattachedPresenter ?? new Rendering.WriteableBitmapPresenter(), _logger);
        _unattachedPresenter = null;
        _pipeline = new Rendering.FramePipeline(_lifetime, _view.Dispatcher, _presentation, _logger, NotifyFrameCommitted, _view.NotifyDisplayFailed);
        checkpoint?.Invoke(ViewerInitializationStage.PipelineCreated);
        _hud = new HudTextCollection(_view.HudLayer, _lifetime);
        _menuSession = new Menus.MenuSnapshotSession(_pipeline, _lifetime, _view.Dispatcher, _cleanupErrors.Enqueue);

        _queryScheduler = new(TryAcquireCurrentFrame, _logger, new Imaging.Queries.DispatcherQueryRuntime(_view.Dispatcher));
        _measurementRuntime = new MeasurementRuntime(_lifetime, _view.Dispatcher, _queryScheduler, _logger);
        _measurements = new MeasurementCollection(_view.Layers.Measurements, _measurementRuntime);
        _tools = new MeasurementToolRegistry();
        _measurements.ItemCompleted += _view.NotifyMeasurementCompleted;
        _measurements.ItemRemoved += _view.NotifyMeasurementRemoved;
        _measurements.ItemChanged += _view.NotifyMeasurementChanged;

        checkpoint?.Invoke(ViewerInitializationStage.MeasurementsCreated);
        var editor = new MeasurementEditController(_view.MeasurementOverlay);
        var input = new ViewerInputBinding(_view.ImageViewport, _view.MeasurementOverlay, _measurements, _view.Layers.Collection, logger: _logger);
        _interaction = new MeasurementInteractionCoordinator(input, editor, _tools, _measurements, _view.Layers.Measurements,
            _measurementRuntime, TryAcquireCurrentFrame);
        input.Connect(_interaction);
        _tools.RegisterTool(new LengthTool());
        _tools.RegisterTool(new PointTool());
        _tools.RegisterTool(new RectangleRoiTool());
        _tools.RegisterTool(new LineProfileTool());
        _pixelInfo = new PixelInfoController(_view.ImageViewport, _view.HudLayer, _queryScheduler);
        _pixelInfo.Enable();
        _menuManager = new MenuManager(_view, _lifetime, _logger);
        _menuController = new ViewerMenuController(_menuManager, _interaction, _tools, _view.Layers,
            _menuSession, _snapshotCapture, _pixelInfo);
        checkpoint?.Invoke(ViewerInitializationStage.MenusCreated);
    }

    internal void AfterNotifications(Action action)
    {
        if (_measurements != null) _measurements.Notifications.AfterNotifications(action);
        else action();
    }

    private void OnDispatcherShutdown(object? sender, EventArgs args)
    {
        _cleanupErrors.Enqueue(new InvalidOperationException("The host dispatcher stopped before the control was disposed."));
        StopOnUiThread();
        _ = BeginDisposal();
    }

    private void NotifyFrameCommitted(Frames.FrameLease frame, Frames.FrameSubmissionOptions? options)
    {
        try
        {
            using var borrowed = frame.Acquire();
            options?.OnCommitted?.Invoke(borrowed);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Frame notification failed"); }
        _view.NotifyFrameCommitted(frame.Info);
    }

    internal void StopOnUiThread()
    {
        _view.Dispatcher.VerifyAccess();
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
        Cleanup(() => _view.Layers?.Collection.Close());
        Cleanup(() => _hud?.Dispose());
        Cleanup(() => _presentation?.Dispose());
        if (_presentation == null) Cleanup(() => _unattachedPresenter?.Dispose());
        _unattachedPresenter = null;
        _view.Dispatcher.ShutdownStarted -= OnDispatcherShutdown;
        AfterNotifications(() =>
        {
            _view.ClearNotifications();
            _stopped.TrySetResult();
        });
    }

    internal void RecordCleanupFailure(Exception error) => _cleanupErrors.Enqueue(error);

    private void Cleanup(Action action)
    {
        try { action(); } catch (Exception ex) { _cleanupErrors.Enqueue(ex); }
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            if (!_closed)
            {
                if (_view.Dispatcher.CheckAccess()) StopOnUiThread();
                else
                {
                    try { await _view.Dispatcher.InvokeAsync(StopOnUiThread).Task.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (_closed) { }
                }
            }
            await _stopped.Task.ConfigureAwait(false);
            var drained = Task.WhenAll(_pipeline?.Completion ?? Task.CompletedTask,
                _queryScheduler?.Completion ?? Task.CompletedTask);
            try { await drained.ConfigureAwait(false); }
            catch (Exception ex)
            {
                if (drained.Exception is { } aggregate)
                    foreach (var error in aggregate.InnerExceptions) _cleanupErrors.Enqueue(error);
                else _cleanupErrors.Enqueue(ex);
            }
            _lifetime.Complete();
            var errors = _cleanupErrors.ToArray();
            if (errors.Length == 1) _disposeCompletion.TrySetException(errors[0]);
            else if (errors.Length > 1) _disposeCompletion.TrySetException(new AggregateException("Viewer resources failed to close.", errors));
            else _disposeCompletion.TrySetResult();
        }
        catch (Exception ex) { _disposeCompletion.TrySetException(ex); }
    }

    internal ValueTask BeginDisposal()
    {
        _lifetime.BeginDisposal();
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0) _ = CompleteDisposalAsync();
        return new(_disposeCompletion.Task);
    }
}
