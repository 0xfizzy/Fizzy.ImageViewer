using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Viewport;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Rendering;

/// <summary>Owns submission and display state. Commits, freeze and stop run on the viewer STA.</summary>
internal sealed class FramePipeline
{
    private object _frameGate => _lifetime.Gate;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ViewerLifetime _lifetime;
    private readonly Dispatcher _dispatcher;
    private readonly FramePresentation _presentation;
    private readonly ILogger _logger;
    private readonly Action<FrameLease, FrameSubmissionOptions?> _onCommitted;
    private readonly Action<DisplayFailure> _onDisplayFailed;
    private bool _closed, _displayEnabled;
    private int _acceptedSubmissions;
    private TaskCompletionSource? _submissionsDrained;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _releaseErrors = new();

    internal FramePipeline(ViewerLifetime lifetime, Dispatcher dispatcher, FramePresentation presentation,
        ILogger logger, Action<FrameLease, FrameSubmissionOptions?> onCommitted, Action<DisplayFailure> onDisplayFailed)
    {
        _lifetime = lifetime;
        _dispatcher = dispatcher;
        _presentation = presentation;
        _logger = logger;
        _onCommitted = onCommitted;
        _onDisplayFailed = onDisplayFailed;
    }

    internal Task Completion { get { lock (_frameGate) return _renderTask; } }
    private FrameLease? _currentFrame;
    private Submission? _pending;
    private Task _renderTask = Task.CompletedTask;
    private bool _running, _isFrozen, _redraw;
    private long _nextFrameId, _freezeEpoch, _displayVersion, _committedDisplayVersion;
    private GrayDisplayRange? _grayRange, _committedGrayRange;

    private sealed record Submission(FrameLease Frame, FrameSubmissionOptions? Options, long Epoch);

    internal ValueTask<FrameSubmitResult> SubmitAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var lease = frame.Transfer();
        Submission? replaced;
        FrameSubmitStatus status;
        bool admitted;
        lock (_frameGate)
        {
            lease.Info = new(++_nextFrameId, lease.Descriptor, options?.SourceTimestamp);
            admitted = !_lifetime.IsStopping;
            if (admitted) _acceptedSubmissions++;
            status = !admitted ? FrameSubmitStatus.Closed : cancellationToken.IsCancellationRequested ? FrameSubmitStatus.Cancelled :
                _isFrozen ? FrameSubmitStatus.Frozen : FrameSubmitStatus.Accepted;
            replaced = status == FrameSubmitStatus.Accepted ? _pending : null;
            if (status == FrameSubmitStatus.Accepted)
            {
                _pending = new(lease, options, _freezeEpoch);
                StartRenderLoop();
            }
        }
        Exception? error = null;
        try
        {
            // Replaced storage already belongs to the pipeline, not to this new receipt.
            ReleaseFrame(replaced?.Frame);
            if (status != FrameSubmitStatus.Accepted)
                try { lease.Dispose(); } catch (Exception ex) { error = ex; }
            return new(new FrameSubmitResult(lease.Info.FrameId, status, error));
        }
        finally
        {
            if (admitted)
                lock (_frameGate)
                    if (--_acceptedSubmissions == 0) _submissionsDrained?.TrySetResult();
        }
    }

    internal void SetDisplayEnabled(bool enabled)
    {
        _dispatcher.VerifyAccess();
        lock (_frameGate)
        {
            if (_lifetime.IsStopping || _displayEnabled == enabled) return;
            _displayEnabled = enabled;
            StartRenderLoop();
        }
    }

    internal FrameLease? AcquireCurrentFrame()
    {
        lock (_frameGate) { _lifetime.ThrowIfStopping(); return _currentFrame?.Acquire(); }
    }

    internal FrameLease? TryAcquireCurrentFrame()
    {
        lock (_frameGate) return _lifetime.IsStopping ? null : _currentFrame?.Acquire();
    }

    internal GrayDisplayRange? DisplayRange
    {
        get { lock (_frameGate) { _lifetime.ThrowIfStopping(); return _grayRange; } }
    }

    internal void SetDisplayRange(GrayDisplayRange? value)
    {
        _dispatcher.VerifyAccess();
        if (value is { } range) _ = new GrayDisplayRange(range.Minimum, range.Maximum);
        lock (_frameGate)
        {
            _lifetime.ThrowIfStopping();
            if (value != null && _currentFrame is { } current && current.D3D9Surface != 0)
                throw new NotSupportedException("GPU surface display mapping must be applied by the GPU producer.");
            _grayRange = value;
            _displayVersion++;
            _redraw = _currentFrame != null;
            StartRenderLoop();
        }
    }

    private void StartRenderLoop()
    {
        if (_running || !_displayEnabled || _isFrozen || _lifetime.IsStopping || (_pending == null && !_redraw)) return;
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
                if (!_displayEnabled || _isFrozen || _lifetime.IsStopping) { _running = false; return; }
                submission = _pending;
                _pending = null;
                frame = submission?.Frame ?? (_redraw ? _currentFrame?.Acquire() : null);
                if (submission == null) _redraw = false;
                range = _grayRange;
                version = _displayVersion;
                if (frame == null) { _running = false; return; }
            }
            bool deferred = false;
            try
            {
                using var prepared = _presentation.Prepare(frame, range, _shutdown.Token);
                deferred = await _dispatcher.InvokeAsync(() => Commit(frame, submission, prepared, range, version),
                    DispatcherPriority.Render, _shutdown.Token).Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frame {FrameId} display failed", frame.Info.FrameId);
                try
                {
                    await _dispatcher.InvokeAsync(() => _onDisplayFailed(new(frame.Info, range, version, ex)),
                        DispatcherPriority.Render, _shutdown.Token).Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { _releaseErrors.Enqueue(ex); }
                catch (Exception notificationError) { _releaseErrors.Enqueue(new AggregateException(ex, notificationError)); }
                lock (_frameGate)
                    if (version == _displayVersion) _redraw = false;
            }
            finally
            {
                bool retained = false;
                lock (_frameGate)
                {
                    if (deferred && !_lifetime.IsStopping)
                    {
                        if (submission != null && !_isFrozen && submission.Epoch == _freezeEpoch && _pending == null)
                        {
                            _pending = submission;
                            retained = true;
                        }
                        else if (submission == null) _redraw = true;
                    }
                }
                if (!retained) ReleaseFrame(frame);
            }
        }
    }

    // True means visibility deferred this attempt; it may return to the latest slot.
    private bool Commit(FrameLease frame, Submission? submission, FramePresentation.PreparedFrame prepared, GrayDisplayRange? range, long version)
    {
        FrameLease? previous = null;
        bool resized;
        lock (_frameGate)
        {
            if (_lifetime.IsStopping) return false;
            if (submission != null)
            {
                if (_isFrozen || submission.Epoch != _freezeEpoch) return false;
            }
            else if (_currentFrame?.Info.FrameId != frame.Info.FrameId) return false;
            if (!_displayEnabled || _isFrozen) return true;
            resized = _currentFrame?.Descriptor.Width != frame.Descriptor.Width || _currentFrame?.Descriptor.Height != frame.Descriptor.Height;
        }
        // UI commits/freeze/close are serialized by the Dispatcher. Never hold the input
        // gate while uploading pixels: acquisition must be able to replace the pending frame.
        var source = _presentation.Present(prepared);
        lock (_frameGate)
        {
            _presentation.SetImage(source, frame.Descriptor);
            previous = _currentFrame;
            _currentFrame = frame.Acquire();
            _redraw = version != _displayVersion;
            _committedDisplayVersion = version;
            _committedGrayRange = range;
        }
        ReleaseFrame(previous);
        if (resized) _presentation.FitToViewport();
        if (submission != null) _onCommitted(frame, submission.Options);
        return false;
    }

    private Exception? ReleaseFrame(FrameLease? frame)
    {
        try { frame?.Dispose(); return null; }
        catch (Exception ex) { _releaseErrors.Enqueue(ex); return ex; }
    }

    // The frame and display settings are captured under the same gate as publication.
    internal CommittedViewLease? AcquireCommittedView()
    {
        lock (_frameGate)
        {
            _lifetime.ThrowIfStopping();
            return AcquireCommittedViewCore();
        }
    }

    private CommittedViewLease? AcquireCommittedViewCore() => _currentFrame == null ? null :
        new(_currentFrame.Acquire(), _committedGrayRange, _committedDisplayVersion);

    internal CommittedViewLease? FreezeAndAcquire()
    {
        _dispatcher.VerifyAccess();
        Submission? pending;
        CommittedViewLease? view;
        lock (_frameGate)
        {
            _lifetime.ThrowIfStopping();
            _isFrozen = true;
            _freezeEpoch++;
            pending = _pending;
            _pending = null;
            view = AcquireCommittedViewCore();
        }
        ReleaseFrame(pending?.Frame);
        return view;
    }

    internal void Resume()
    {
        _dispatcher.VerifyAccess();
        lock (_frameGate) { _isFrozen = false; StartRenderLoop(); }
    }

    internal void StopOnUiThread()
    {
        _dispatcher.VerifyAccess();
        Submission? pending;
        FrameLease? current;
        lock (_frameGate)
        {
            if (_closed) return;
            _lifetime.BeginDisposal();
            _closed = true;
            pending = _pending;
            _pending = null;
            current = _currentFrame;
            _currentFrame = null;
            _redraw = false;
        }
        try { _shutdown.Cancel(); } catch (Exception ex) { _releaseErrors.Enqueue(ex); }
        ReleaseFrame(pending?.Frame);
        ReleaseFrame(current);
        lock (_frameGate)
        {
            var drained = _acceptedSubmissions == 0 ? Task.CompletedTask :
                (_submissionsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            _renderTask = CompleteStopAsync(_renderTask, drained);
        }
    }

    private async Task CompleteStopAsync(Task render, Task submissionsDrained)
    {
        try
        {
            await Task.WhenAll(render, submissionsDrained).ConfigureAwait(false);
            if (!_releaseErrors.IsEmpty) throw new AggregateException("Frame release failed.", _releaseErrors);
        }
        finally { _shutdown.Dispose(); }
    }
}
