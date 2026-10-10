using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Cuda;

/// <summary>Constructs owned GPU frames with display surfaces and original-pixel access.</summary>
public static class CudaImageFrame
{
    /// <summary>Immediately takes ownership of a nonnull source, including on validation failure or cancellation.
    /// Returns an immutable WPF-compatible frame after GPU writes and submission completion.
    /// The final frame reference releases the source and surface token. No CPU image fallback is performed.</summary>
    public static ValueTask<ImageFrame> CreateAsync(ICudaFrameSource source, CancellationToken cancellationToken = default)
        => CreateAsync(source, CudaBackend.Instance, cancellationToken);

    internal static async ValueTask<ImageFrame> CreateAsync(ICudaFrameSource source, ICudaBackend backend, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        IDisposable? token = null;
        try
        {
            var descriptor = source.Descriptor;
            descriptor.Validate(descriptor.RequiredBytes);
            int format = CudaBackend.Format(descriptor.Format);
            ct.ThrowIfCancellationRequested();
            nint surface = 0;
            await source.ExecuteAsync(scope =>
            {
                CudaBackend.Validate(scope, descriptor);
                var resource = backend.CreateSurface(scope.Context, descriptor.Width, descriptor.Height);
                try { token = scope.RetainResource(resource); }
                catch (Exception error) { Cleanup(error, resource.Release); throw; }
                surface = resource.Surface;
                backend.WriteSurface(resource, scope, descriptor, format);
                scope.WaitForCompletion();
            }, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (token is null || surface == 0) throw new InvalidOperationException("The bridge did not execute the CUDA callback.");
            // The factory owns release even if its construction fails.
            var pixels = new CudaPixelSource(source, descriptor, backend);
            var transferred = token;
            token = null;
            var transferredSource = source;
            source = null!;
            return ImageFrame.TakeD3D9Surface(descriptor, surface, pixels,
                () => Release(transferred, transferredSource));
        }
        catch (Exception error)
        {
            Cleanup(error, () => Release(token, source));
            throw;
        }
    }

    private static void Release(IDisposable? token, ICudaFrameSource? source)
    {
        Exception? error = null;
        try { token?.Dispose(); } catch (Exception ex) { error = ex; }
        try { source?.Dispose(); }
        catch (Exception ex) { if (error != null) throw new AggregateException(error, ex); throw; }
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static void Cleanup(Exception error, Action cleanup)
    {
        try { cleanup(); } catch (Exception failure) { throw new AggregateException(error, failure); }
    }
}
