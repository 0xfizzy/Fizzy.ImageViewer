using System.Runtime.InteropServices;

namespace Fizzy.ImageViewer.Cuda;

internal static partial class NativeMethods
{
    internal const int Abi = 1;
    private const string Dll = "Fizzy.ImageViewer.Cuda.Native";
    internal static void Check(int status)
    {
        if (status == 0) return;
        throw new CudaException(Marshal.PtrToStringUTF8(fv_error()) ?? "CUDA extension failure",
            fv_error_domain() switch { 1 => "CUDA runtime", 2 => "CUDA driver", 3 => "D3D9", _ => "extension" }, fv_error_code());
    }
    internal static void VerifyAbi()
    {
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("CUDA images require Windows x64.");
        int actual = fv_abi_version();
        if (actual != Abi) throw new NotSupportedException($"CUDA extension ABI mismatch: expected {Abi}, received {actual}.");
    }
    [LibraryImport(Dll)] internal static partial int fv_abi_version();
    [LibraryImport(Dll)] private static partial nint fv_error();
    [LibraryImport(Dll)] private static partial int fv_error_domain();
    [LibraryImport(Dll)] private static partial int fv_error_code();
    [LibraryImport(Dll)] internal static partial int fv_display_create(nint context, int width, int height, out nint owner, out nint surface);
    [LibraryImport(Dll)] internal static partial int fv_display_write(nint owner, nint stream, nint input, long pitch, int format, nint scratch);
    [LibraryImport(Dll)] internal static partial int fv_display_release(nint owner);
    [LibraryImport(Dll)] internal static partial long fv_display_live_surfaces();
    [LibraryImport(Dll)] internal static partial int fv_query(nint context, nint stream, nint input, long pitch,
        int width, int height, int channels, int kind, nint coordinates, int count,
        int x, int y, int regionWidth, int regionHeight, nint deviceCoordinates, nint scratch, nint output, nint host);
}
