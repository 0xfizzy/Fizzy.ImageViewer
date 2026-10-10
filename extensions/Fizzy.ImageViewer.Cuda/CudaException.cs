namespace Fizzy.ImageViewer.Cuda;

/// <summary>A CUDA extension native failure. Consumer execution exceptions are propagated unchanged.</summary>
public sealed class CudaException : Exception
{
    /// <summary>Creates a failure with the native operation's domain and code.</summary>
    public CudaException(string message, string domain, int nativeCode) : base(message)
    {
        Domain = domain;
        NativeCode = nativeCode;
    }
    /// <summary>Native error domain: CUDA runtime, CUDA driver, D3D9 or extension.</summary>
    public string Domain { get; }
    /// <summary>Domain-specific error code.</summary>
    public int NativeCode { get; }
}
