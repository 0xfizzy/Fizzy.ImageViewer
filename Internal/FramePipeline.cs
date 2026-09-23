using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Internal;

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
    private bool _closed;

    internal FramePipeline(ViewerLifetime lifetime, Dispatcher dispatcher, FramePresentation presentation,
        ILogger logger, Action<FrameLease, FrameSubmissionOptions?> onCommitted)
    {
        _lifetime = lifetime;
        _dispatcher = dispatcher;
        _presentation = presentation;
        _logger = logger;
        _onCommitted = onCommitted;
    }

    internal Task Completion { get { lock (_frameGate) return _renderTask; } }
    private FrameLease? _currentFrame;
    private Submission? _pending;
    private Task _renderTask = Task.CompletedTask;
    private bool _running, _isFrozen, _redraw;
    private long _nextFrameId, _freezeEpoch, _displayVersion, _committedDisplayVersion;
    private GrayDisplayRange? _grayRange, _committedGrayRange;

    private sealed class Submission(FrameLease frame, FrameSubmissionOptions? options, CancellationToken token, long epoch)
    {
        public FrameLease Frame { get; } = frame;
        public FrameSubmissionOptions? Options { get; } = options;
        public CancellationToken Token { get; } = token;
        public long Epoch { get; } = epoch;
        public TaskCompletionSource<FrameSubmitResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal ValueTask<FrameSubmitResult> SubmitAsync(ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken ct = default)
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
            rejected = _lifetime.IsStopping ? FrameSubmitStatus.Closed : ct.IsCancellationRequested ? FrameSubmitStatus.Cancelled :
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

    internal FrameLease? AcquireCurrentFrame()
    {
        lock (_frameGate) { _lifetime.ThrowIfStopping(); return _currentFrame?.Acquire(); }
    }

    internal FrameLease? AcquireCurrentFrameForMeasurement()
    {
        lock (_frameGate) return _lifetime.IsStopping ? null : _currentFrame?.Acquire();
    }

    internal GrayDisplayRange? DisplayRange
    {
        get { lock (_frameGate) { _lifetime.ThrowIfStopping(); return _grayRange; } }
        set
        {
            if (value is { } range) _ = new GrayDisplayRange(range.Minimum, range.Maximum);
            lock (_frameGate)
            {
                _lifetime.ThrowIfStopping();
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
                using var prepared = _presentation.Prepare(frame, range, linked.Token);
                status = await _dispatcher.InvokeAsync(() => Commit(frame, submission, prepared, range, version), DispatcherPriority.Render, linked.Token).Task.ConfigureAwait(false);
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

    private FrameSubmitStatus Commit(FrameLease frame, Submission? submission, FramePresentation.PreparedFrame prepared, GrayDisplayRange? range, long version)
    {
        FrameLease? previous = null;
        bool resized;
        lock (_frameGate)
        {
            if (_lifetime.IsStopping) return FrameSubmitStatus.Closed;
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
        var source = _presentation.Present(prepared);
        lock (_frameGate)
        {
            _presentation.SetImage(source, frame.Descriptor);
            previous = _currentFrame;
            _currentFrame = frame.Acquire();
            _committedDisplayVersion = version;
            _committedGrayRange = range;
        }
        ReleaseFrame(previous);
        if (resized) _presentation.FitToContainer();
        if (submission != null) _onCommitted(frame, submission.Options);
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

    // The frame and display settings are captured under the same gate as publication.
    internal CommittedFrameLease? AcquireCommittedView()
    {
        lock (_frameGate)
        {
            _lifetime.ThrowIfStopping();
            return AcquireCommittedViewCore();
        }
    }

    private CommittedFrameLease? AcquireCommittedViewCore() => _currentFrame == null ? null :
        new(_currentFrame.Acquire(), _committedGrayRange, _committedDisplayVersion);

    internal CommittedFrameLease? FreezeAndAcquire()
    {
        _dispatcher.VerifyAccess();
        Submission? pending;
        CommittedFrameLease? view;
        lock (_frameGate)
        {
            _lifetime.ThrowIfStopping();
            _isFrozen = true;
            _freezeEpoch++;
            pending = _pending;
            _pending = null;
            view = AcquireCommittedViewCore();
        }
        if (pending != null) Finish(pending, FrameSubmitStatus.Frozen);
        return view;
    }

    internal void Resume()
    {
        _dispatcher.VerifyAccess();
        lock (_frameGate) _isFrozen = false;
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
        _shutdown.Cancel();
        if (pending != null) Finish(pending, FrameSubmitStatus.Closed);
        ReleaseFrame(current);
        lock (_frameGate) _renderTask = CompleteStopAsync(_renderTask);
    }

    private async Task CompleteStopAsync(Task render)
    {
        try { await render.ConfigureAwait(false); }
        finally { _shutdown.Dispose(); }
    }
}
