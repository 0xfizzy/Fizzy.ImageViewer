using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Rendering;

/// <summary>Prepares pixels off STA; owns presentation resources exclusively on the viewer STA.</summary>
internal sealed class FramePresentation(Dispatcher dispatcher, ImageLayer layer, ICpuImagePresenter presenter, ILogger logger) : IDisposable
{
    private D3DImagePresenter? _d3dPresenter;
    private bool _disposed;

    // Frame is borrowed from the render operation, which outlives this prepared result.
    internal sealed class PreparedFrame(FrameLease frame, DisplayBuffer? pixels) : IDisposable
    {
        internal FrameLease Frame { get; } = frame;
        internal DisplayBuffer? Pixels { get; } = pixels;
        public void Dispose() => Pixels?.Dispose();
    }

    internal PreparedFrame Prepare(FrameLease frame, GrayDisplayRange? range, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (frame.D3D9Surface != 0 && range != null)
            throw new NotSupportedException("GPU surface display mapping must be applied by the GPU producer.");
        return new(frame, frame.D3D9Surface == 0 ? DisplayConverter.Convert(frame, range, ct) : null);
    }

    internal ImageSource Present(PreparedFrame prepared)
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var frame = prepared.Frame;
        var source = frame.D3D9Surface != 0
            ? (_d3dPresenter ??= new D3DImagePresenter()).Present(frame)
            : presenter.Present(prepared.Pixels!);
        if (frame.D3D9Surface == 0 && _d3dPresenter != null)
        {
            var previous = _d3dPresenter;
            _d3dPresenter = null;
            Cleanup(previous.Dispose);
        }
        return source;
    }

    // Called inside the pipeline's publication gate, after the expensive upload has finished.
    internal void SetImage(ImageSource source, FrameDescriptor descriptor)
    {
        dispatcher.VerifyAccess();
        layer.SetImage(source, descriptor.Width, descriptor.Height);
    }

    internal void FitToContainer()
    {
        dispatcher.VerifyAccess();
        Cleanup(layer.FitImageToContainer);
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        Cleanup(presenter.Dispose);
        Cleanup(() => _d3dPresenter?.Dispose());
        _d3dPresenter = null;
    }

    private void Cleanup(Action action)
    {
        try { action(); }
        catch (Exception ex) { logger.LogWarning(ex, "Presentation cleanup failed"); }
    }
}
