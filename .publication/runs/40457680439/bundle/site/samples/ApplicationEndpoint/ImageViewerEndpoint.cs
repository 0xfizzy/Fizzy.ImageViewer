using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Viewport;

namespace ApplicationEndpoint;

internal sealed class ImageViewerEndpoint : IImageSink, IImageDisplay, IImageNotifications
{
    private readonly object _gate = new();
    private IViewerCapabilities? _viewer;
    private bool _attached, _stopped;

    public event EventHandler<LengthResult>? LengthCompleted;
    public event EventHandler<string>? DisplayFailed;

    // Composition only: called once on the control's UI thread. This borrows the viewer.
    public void Attach(IViewerCapabilities viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        lock (_gate)
        {
            if (_attached || _stopped) throw new InvalidOperationException("This endpoint cannot be bound again.");
            _attached = true;
            _viewer = viewer; // Retain for cleanup even if subscribing fails midway.
        }
        viewer.MeasurementCompleted += OnMeasurementCompleted;
        viewer.DisplayFailed += OnDisplayFailed;
    }

    public void StopAccepting()
    {
        lock (_gate) _stopped = true;
    }

    // UI host calls only after its producer has ended. No independent drain protocol.
    public void Detach()
    {
        IViewerCapabilities? viewer;
        lock (_gate)
        {
            if (!_stopped) throw new InvalidOperationException("Stop the endpoint before detaching.");
            viewer = _viewer;
            _viewer = null;
        }
        if (viewer is null) return;
        try { viewer.MeasurementCompleted -= OnMeasurementCompleted; }
        finally { viewer.DisplayFailed -= OnDisplayFailed; }
    }

    private IViewerCapabilities GetViewer()
    {
        lock (_gate)
        {
            if (_stopped) throw new DisplaySessionStoppedException();
            return _viewer ?? throw new InvalidOperationException("The display session is not bound.");
        }
    }

    public async Task PublishAsync(GrayFrame input, CancellationToken cancellationToken)
    {
        bool transferred = false;
        try
        {
            var viewer = GetViewer();
            cancellationToken.ThrowIfCancellationRequested();
            var pixels = input.Pixels;
            var descriptor = new FrameDescriptor(input.Width, input.Height, input.Stride, FramePixelFormat.Gray8);
            // TakeOwnership also releases on validation failure. From here the library owns cleanup.
            transferred = true;
            using var frame = ImageFrame.TakeOwnership(descriptor, pixels, input.Dispose);
            var result = await viewer.SubmitFrameAsync(frame, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Error is { } error) throw error;
            if (result.Status == FrameSubmitStatus.Cancelled) throw new OperationCanceledException(cancellationToken);
            if (result.Status != FrameSubmitStatus.Accepted)
                throw new InvalidOperationException($"Frame reception failed: {result.Status}");
        }
        catch (Exception error) when (!transferred)
        {
            try { input.Dispose(); }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
    }

    public void FitImage() => GetViewer().FitToViewport();
    public void StartLengthMeasurement() => GetViewer().ActivateMeasurementTool(MeasurementToolIds.Length);

    private void OnMeasurementCompleted(object? sender, MeasurementEventArgs args)
    {
        if (args.Snapshot.Origin.ToolId == MeasurementToolIds.Length && args.Snapshot.Geometry is LineMeasurementGeometry line)
            LengthCompleted?.Invoke(this, new LengthResult(line.Length));
    }

    private void OnDisplayFailed(object? sender, DisplayFailure failure)
        => DisplayFailed?.Invoke(this, failure.Error.Message);
}
