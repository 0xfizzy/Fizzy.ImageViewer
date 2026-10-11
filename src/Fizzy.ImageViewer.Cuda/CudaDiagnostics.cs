namespace Fizzy.ImageViewer.Cuda;

/// <summary>Process-wide extension diagnostics; metrics do not control lifetime or admission.</summary>
public static class CudaDiagnostics
{
    private static long _queries, _bytes, _live, _resultBytes;
    /// <summary>Gets independently sampled counters without loading CUDA.</summary>
    public static CudaQueryMetrics QueryMetrics => new(Interlocked.Read(ref _queries), Interlocked.Read(ref _bytes),
        Interlocked.Read(ref _live), Interlocked.Read(ref _resultBytes));
    /// <summary>Gets native surface owners still alive, including isolated resources. Loads the native DLL.</summary>
    public static long GetLiveSurfaceCount() { NativeMethods.VerifyAbi(); return NativeMethods.fv_display_live_surfaces(); }
    internal static void Start(int length) { Interlocked.Increment(ref _live); Interlocked.Add(ref _resultBytes, length); }
    internal static void Enqueued(int length) { Interlocked.Increment(ref _queries); Interlocked.Add(ref _bytes, length); }
    internal static void End(int length) { Interlocked.Decrement(ref _live); Interlocked.Add(ref _resultBytes, -length); }
}
