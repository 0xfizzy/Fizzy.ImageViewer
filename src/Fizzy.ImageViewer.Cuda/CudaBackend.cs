using System.Runtime.InteropServices;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Cuda;

internal sealed class CudaBackend : ICudaBackend
{
    internal static readonly CudaBackend Instance = new();
    internal static int Format(FramePixelFormat format) => format switch
    {
        FramePixelFormat.Gray8 => 0, FramePixelFormat.Bgr24 => 1, FramePixelFormat.Rgb24 => 2,
        FramePixelFormat.Bgra32 => 3, FramePixelFormat.Pbgra32 => 4, FramePixelFormat.Rgba32 => 5,
        _ => throw new NotSupportedException($"CUDA input format {format} is not supported.")
    };
    internal static void Validate(ICudaExecutionScope scope, FrameDescriptor descriptor)
    {
        if (scope.Context == 0 || scope.InputPointer == 0 || scope.RowStride < descriptor.RowBytes ||
            scope.InputByteLength < checked((descriptor.Height - 1L) * scope.RowStride + descriptor.RowBytes))
            throw new ArgumentException("The CUDA bridge returned an invalid device image view.");
    }
    private static nint Allocate(ICudaExecutionScope scope, long bytes, CudaMemoryKind kind)
    {
        var pointer = scope.AllocateScratch(bytes, kind);
        if (pointer == 0) throw new InvalidOperationException("The CUDA bridge returned null scratch storage.");
        return pointer;
    }
    public CudaSurface CreateSurface(nint context, int width, int height)
    {
        NativeMethods.VerifyAbi();
        NativeMethods.Check(NativeMethods.fv_display_create(context, width, height, out var owner, out var surface));
        try { return new(owner, surface, pointer => NativeMethods.Check(NativeMethods.fv_display_release(pointer))); }
        catch (Exception error)
        {
            try { NativeMethods.Check(NativeMethods.fv_display_release(owner)); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
    }
    public void WriteSurface(CudaSurface surface, ICudaExecutionScope scope, FrameDescriptor descriptor, int format)
    {
        // Premultiplied BGRA already has the exact display layout.
        nint scratch = descriptor.Format == FramePixelFormat.Pbgra32 ? 0 : Allocate(scope, checked((long)descriptor.Width * descriptor.Height * 4), CudaMemoryKind.Device);
        NativeMethods.Check(NativeMethods.fv_display_write(surface.Owner, scope.Stream, scope.InputPointer, scope.RowStride, format, scratch));
    }
    public byte[] Query(ICudaExecutionScope scope, FrameDescriptor descriptor, int kind, int[]? points, PixelRegion region)
    {
        Validate(scope, descriptor);
        NativeMethods.VerifyAbi();
        int channels = descriptor.Format.BytesPerPixel();
        int count = (points?.Length ?? 0) / 2;
        int length = kind == 0 ? checked(count * channels) : kind == 1 ? channels * 32 : checked(region.Width * region.Height * channels);
        nint host = Allocate(scope, length, CudaMemoryKind.PinnedHost);
        nint coordinates = 0, deviceCoordinates = 0, scratch = 0, output = 0;
        if (kind == 0)
        {
            long bytes = checked(count * 8L);
            coordinates = Allocate(scope, bytes, CudaMemoryKind.PinnedHost);
            Marshal.Copy(points!, 0, coordinates, points!.Length);
            deviceCoordinates = Allocate(scope, bytes, CudaMemoryKind.Device);
            output = Allocate(scope, length, CudaMemoryKind.Device);
        }
        else if (kind == 1)
        {
            long blocks = Math.Min(256, ((long)region.Width * region.Height + 255) / 256);
            scratch = Allocate(scope, blocks * channels * 32, CudaMemoryKind.Device);
            output = Allocate(scope, length, CudaMemoryKind.Device);
        }
        CudaDiagnostics.Start(length);
        try
        {
            NativeMethods.Check(NativeMethods.fv_query(scope.Context, scope.Stream, scope.InputPointer, scope.RowStride,
                descriptor.Width, descriptor.Height, channels, kind, coordinates, count, region.X, region.Y, region.Width, region.Height,
                deviceCoordinates, scratch, output, host));
            CudaDiagnostics.Enqueued(length);
            scope.WaitForGpuCompletion();
            var bytes = new byte[length];
            Marshal.Copy(host, bytes, 0, length);
            return bytes;
        }
        finally { CudaDiagnostics.End(length); }
    }
}
