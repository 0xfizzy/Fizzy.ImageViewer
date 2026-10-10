using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Rendering;

/// <summary>Prepares pixels off STA; owns presentation resources exclusively on the viewer STA.</summary>
internal sealed class FramePresentation(Dispatcher dispatcher, ImageViewport layer, ICpuImagePresenter presenter, ILogger logger) : IDisposable
{
    private D3DImagePresenter? _d3dPresenter;
    private List<Exception>? _frontBufferFailures;
    private bool _disposed;

    // Frame is borrowed from the render operation, which outlives this prepared result.
    internal sealed class PreparedFrame(FrameLease frame, DisplayBuffer? pixels) : IDisposable
    {
        internal FrameLease Frame { get; } = frame;
        internal DisplayBuffer? Pixels { get; } = pixels;
        public void Dispose() => Pixels?.Dispose();
    }

    internal PreparedFrame Prepare(FrameLease frame, GrayDisplayRange? range, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.D3D9Surface != 0 && range != null)
            throw new NotSupportedException("GPU surface display mapping must be applied by the GPU producer.");
        return new(frame, frame.D3D9Surface == 0 ? DisplayConverter.Convert(frame, range, cancellationToken) : null);
    }

    internal ImageSource Present(PreparedFrame prepared)
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var frame = prepared.Frame;
        var source = frame.D3D9Surface != 0
            ? (_d3dPresenter ??= new D3DImagePresenter(RecordFrontBufferFailure)).Present(frame)
            : presenter.Present(prepared.Pixels!);
        if (frame.D3D9Surface == 0 && _d3dPresenter != null)
        {
            var previous = _d3dPresenter;
            _d3dPresenter = null;
            previous.Dispose();
        }
        return source;
    }

    // Called inside the pipeline's publication gate, after the expensive upload has finished.
    internal void SetImage(ImageSource source, FrameDescriptor descriptor)
    {
        dispatcher.VerifyAccess();
        layer.SetImage(source, descriptor.Width, descriptor.Height);
    }

    internal void FitToViewport()
    {
        dispatcher.VerifyAccess();
        Cleanup(layer.FitToViewport);
    }

    internal void RecordFrontBufferFailure(Exception error) => (_frontBufferFailures ??= []).Add(error);

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        List<Exception> errors = _frontBufferFailures ?? [];
        // Unlink the visual on its STA. Failed native detach is independently quarantined by D3DImagePresenter.
        try { layer.ImageDisplay.Source = null; } catch (Exception ex) { errors.Add(ex); }
        try { presenter.Dispose(); } catch (Exception ex) { errors.Add(ex); }
        try { _d3dPresenter?.Dispose(); } catch (Exception ex) { errors.Add(ex); }
        _d3dPresenter = null;
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Presentation cleanup failed.", errors);
    }

    private void Cleanup(Action action)
    {
        try { action(); }
        catch (Exception ex) { logger.LogWarning(ex, "Presentation cleanup failed"); }
    }
}
