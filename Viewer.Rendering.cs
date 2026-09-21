using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private readonly object _frameGate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IImagePresenter _presenter;
    private D3DImagePresenter? _d3dPresenter;
    private FrameLease? _currentFrame;
    private Submission? _pending;
    private Task _renderTask = Task.CompletedTask;
    private bool _running, _closed, _isFrozen, _redraw;
    private long _nextFrameId, _freezeEpoch, _displayVersion, _committedDisplayVersion;
    private GrayDisplayRange? _grayRange, _committedGrayRange;
    public event Action<FrameInfo>? FrameCommitted;

    private sealed class Submission(FrameLease frame, FrameSubmissionOptions? options, CancellationToken token, long epoch)
    {
        public FrameLease Frame { get; } = frame;
        public FrameSubmissionOptions? Options { get; } = options;
        public CancellationToken Token { get; } = token;
        public long Epoch { get; } = epoch;
        public TaskCompletionSource<FrameSubmitResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public ValueTask<FrameSubmitResult> SubmitFrameAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var lease = frame.Transfer();
        Submission submission;
        Submission? replaced;
        FrameSubmitStatus? rejected;
        lock (_frameGate)
        {
            lease.Info = new(++_nextFrameId, lease.Descriptor, options?.SourceTimestamp);
            submission = new(lease, options, ct, _freezeEpoch);
            rejected = _closed ? FrameSubmitStatus.Closed : ct.IsCancellationRequested ? FrameSubmitStatus.Cancelled :
                _isFrozen ? FrameSubmitStatus.Frozen : null;
            replaced = rejected == null ? _pending : null;
            if (rejected == null)
            {
                _pending = submission;
                StartRenderLoop();
            }
        }
        if (replaced != null) Finish(replaced, FrameSubmitStatus.Superseded);
        if (rejected.HasValue) Finish(submission, rejected.Value);
        return new(submission.Completion.Task);
    }

    public FrameLease? AcquireCurrentFrame()
    {
        lock (_frameGate) return _currentFrame?.Acquire();
    }

    public GrayDisplayRange? DisplayRange
    {
        get { lock (_frameGate) return _grayRange; }
        set
        {
            if (value is { } range) _ = new GrayDisplayRange(range.Minimum, range.Maximum);
            lock (_frameGate)
            {
                if (_closed) throw new ObjectDisposedException(nameof(Viewer));
                _grayRange = value;
                _displayVersion++;
                _redraw = true;
                StartRenderLoop();
            }
        }
    }

    private void StartRenderLoop()
    {
        if (_running) return;
        _running = true;
        _renderTask = Task.Run(RenderLoopAsync);
    }

    private async Task RenderLoopAsync()
    {
        while (true)
        {
            Submission? submission;
            FrameLease? frame;
            GrayDisplayRange? range;
            long version;
            lock (_frameGate)
            {
                submission = _pending;
                _pending = null;
                frame = submission?.Frame ?? (_redraw && !_closed ? _currentFrame?.Acquire() : null);
                _redraw = false;
                range = _grayRange;
                version = _displayVersion;
                if (frame == null) { _running = false; return; }
            }
            var status = FrameSubmitStatus.Failed;
            Exception? error = null;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, submission?.Token ?? default);
                if (frame.D3D9Surface != 0 && range != null)
                    throw new NotSupportedException("GPU surface display mapping must be applied by the GPU producer.");
                using var pixels = frame.D3D9Surface == 0 ? DisplayConverter.Convert(frame, range, linked.Token) : null;
                status = await _window.Dispatcher.InvokeAsync(() => Commit(frame, submission, pixels, range, version), DispatcherPriority.Render, linked.Token).Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { lock (_frameGate) status = _closed ? FrameSubmitStatus.Closed : FrameSubmitStatus.Cancelled; }
            catch (Exception ex)
            {
                error = ex;
                lock (_frameGate) status = _closed ? FrameSubmitStatus.Closed : FrameSubmitStatus.Failed;
                _logger.LogError(ex, "Frame {FrameId} failed", frame.Info.FrameId);
            }
            finally
            {
                if (submission != null) Finish(submission, status, error);
                else ReleaseFrame(frame);
            }
        }
    }

    private FrameSubmitStatus Commit(FrameLease frame, Submission? submission, DisplayBuffer? pixels, GrayDisplayRange? range, long version)
    {
        FrameLease? previous = null;
        bool resized;
        lock (_frameGate)
        {
            if (_closed) return FrameSubmitStatus.Closed;
            if (submission != null)
            {
                if (submission.Token.IsCancellationRequested) return FrameSubmitStatus.Cancelled;
                if (_isFrozen || submission.Epoch != _freezeEpoch) return FrameSubmitStatus.Frozen;
            }
            else if (_currentFrame?.Info.FrameId != frame.Info.FrameId) return FrameSubmitStatus.Superseded;
            resized = _currentFrame?.Descriptor.Width != frame.Descriptor.Width || _currentFrame?.Descriptor.Height != frame.Descriptor.Height;
        }
        // UI commits/freeze/close are serialized by the Dispatcher. Never hold the input
        // gate while uploading pixels: acquisition must be able to replace the pending frame.
        var source = frame.D3D9Surface != 0
            ? (_d3dPresenter ??= new D3DImagePresenter()).Present(frame)
            : _presenter.Present(pixels!);
        if (frame.D3D9Surface == 0 && _d3dPresenter != null)
        {
            _d3dPresenter.Dispose();
            _d3dPresenter = null;
        }
        lock (_frameGate)
        {
            _window.Layer0.SetImage(source, frame.Descriptor.Width, frame.Descriptor.Height);
            previous = _currentFrame;
            _currentFrame = frame.Acquire();
            _committedDisplayVersion = version;
            _committedGrayRange = range;
        }
        ReleaseFrame(previous);
        if (resized) Cleanup(_window.Layer0.FitImageToContainer);
        if (submission != null)
        {
            try { using var borrowed = frame.Acquire(); submission.Options?.OnCommitted?.Invoke(borrowed); }
            catch (Exception ex) { _logger.LogWarning(ex, "Frame callback failed"); }
            _measureManager?.NotifyFrameCommitted(frame.Info);
            foreach (Action<FrameInfo> handler in FrameCommitted?.GetInvocationList() ?? [])
                try { handler(frame.Info); } catch (Exception ex) { _logger.LogWarning(ex, "Frame subscriber failed"); }
            _pixelInfoOverlay?.RequestUpdate();
        }
        return FrameSubmitStatus.Committed;
    }

    private void Finish(Submission submission, FrameSubmitStatus status, Exception? error = null)
    {
        ReleaseFrame(submission.Frame);
        submission.Completion.TrySetResult(new(submission.Frame.Info.FrameId, status, error));
    }
    private void ReleaseFrame(FrameLease? frame)
    {
        try { frame?.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Frame release failed"); }
    }

    internal void Freeze()
    {
        Submission? pending;
        SnapshotRequest? previous;
        lock (_frameGate)
        {
            _isFrozen = true;
            _freezeEpoch++;
            pending = _pending; _pending = null;
            previous = _menuSnapshot;
            _menuSnapshot = _currentFrame == null ? null : new SnapshotRequest(_currentFrame.Acquire(), _committedGrayRange, _committedDisplayVersion, Gate: _exportGate);
        }
        ReleaseFrame(previous?.Frame);
        if (pending != null) Finish(pending, FrameSubmitStatus.Frozen);
    }
    internal void Unfreeze()
    {
        lock (_frameGate) _isFrozen = false;
        // A menu may raise Closed before Click. Keep its target until input has drained.
        var target = _menuSnapshot;
        _window.Dispatcher.BeginInvoke(() =>
        {
            lock (_frameGate)
            {
                if (_isFrozen || !ReferenceEquals(target, _menuSnapshot)) return;
                _menuSnapshot = null;
            }
            ReleaseFrame(target?.Frame);
        }, DispatcherPriority.ContextIdle);
    }

    private void StopRendering()
    {
        Submission? pending;
        FrameLease? current;
        lock (_frameGate)
        {
            if (_closed) return;
            _closed = true;
            pending = _pending; _pending = null;
            current = _currentFrame; _currentFrame = null;
        }
        _shutdown.Cancel();
        if (pending != null) Finish(pending, FrameSubmitStatus.Closed);
        ReleaseFrame(current);
        ReleaseFrame(_menuSnapshot?.Frame); _menuSnapshot = null;
        Cleanup(() => _pixelInfoOverlay?.Disable());
        Cleanup(() => _measureManager?.Dispose());
        Cleanup(() => _window.Layer1.Clear());
        Cleanup(_presenter.Dispose);
        Cleanup(() => { _d3dPresenter?.Dispose(); _d3dPresenter = null; });
        FrameCommitted = null;
    }

    private void Cleanup(Action action)
    {
        try { action(); } catch (Exception ex) { _logger.LogWarning(ex, "Viewer cleanup failed"); }
    }

    public async ValueTask CloseAsync()
    {
        if (!_window.Dispatcher.HasShutdownStarted)
        {
            try
            {
                await _window.Dispatcher.InvokeAsync(() => { _window.CanUserClose = true; _window.Close(); }).Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException) { }
        }
        Task render;
        lock (_frameGate) render = _renderTask;
        await render.ConfigureAwait(false);
        if (_measureManager != null) await _measureManager.Completion.ConfigureAwait(false);
        await _windowStopped.Task.ConfigureAwait(false);
    }
    public virtual async ValueTask DisposeAsync() { await CloseAsync().ConfigureAwait(false); GC.SuppressFinalize(this); }
}
