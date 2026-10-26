using System.Windows.Threading;

namespace Fizzy.ImageViewer.Frames;

/// <summary>A reusable, dispatcher-owned source for static images and infrequent image changes.</summary>
/// <remarks>Create, update and dispose on the host UI thread. Bind the source itself, not a frame token.
/// Controls on the same dispatcher acquire independent references without copying pixels.
/// Disposal empties this source; delayed binding reads then see no image. It does not clear displayed
/// images or dispose controls. The owner must dispose this source even if it was never bound.</remarks>
public sealed class ImageBindingSource : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private FrameLease? _frame;
    private bool _disposed;
    internal long Version { get; private set; }
    internal event Action? Changed;

    /// <summary>Creates an empty source on the current host STA dispatcher.</summary>
    public ImageBindingSource()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("ImageBindingSource requires an STA thread.");
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    internal void VerifyDispatcher(Dispatcher dispatcher)
    {
        _dispatcher.VerifyAccess();
        if (_dispatcher != dispatcher) throw new InvalidOperationException("The source and control must share a dispatcher.");
    }

    internal ImageFrame? AcquireFrame() => _frame?.Acquire().TransferToFrame();

    /// <summary>Retains an independent reference without consuming the frame or copying its pixels.</summary>
    /// <remarks>Call on the creating UI thread; do not concurrently dispose or submit the input frame.
    /// The caller may dispose its frame after this call. Null releases the source reference and retains
    /// each control's last displayed image without retracting accepted input. Each call publishes a
    /// new update, even for the same frame. Do not use Binding.IsAsync for this source.
    /// Cleanup failures propagate after notifying all controls; the new source value remains installed.
    /// A disposed source rejects updates with ObjectDisposedException.</remarks>
    public void SetFrame(ImageFrame? frame)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Replace(frame?.Acquire(), disposing: false);
    }

    private void Replace(FrameLease? next, bool disposing)
    {
        var previous = _frame;
        _frame = next;
        Version++;
        var subscribers = Changed;
        if (disposing) Changed = null;
        List<Exception>? errors = null;
        foreach (Action subscriber in subscribers?.GetInvocationList() ?? [])
            try { subscriber(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        try { previous?.Dispose(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        if (errors?.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors != null) throw new AggregateException(errors);
    }

    /// <summary>Empties the source and releases its reference once, on its creating UI thread.</summary>
    /// <remarks>Existing control and external references remain valid. Final storage cleanup failure
    /// propagates synchronously; repeated disposal does not retry cleanup. Further binding reads are empty.</remarks>
    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        Replace(null, disposing: true);
    }
}
