namespace Fizzy.ImageViewer.Cuda;

/// <summary>Origin of an extension native failure; NativeCode retains that domain's code.</summary>
public enum CudaErrorDomain
{
    /// <summary>Extension validation or an unrecognized native error domain.</summary>
    Extension = 0,
    /// <summary>CUDA runtime API.</summary>
    CudaRuntime = 1,
    /// <summary>CUDA driver API.</summary>
    CudaDriver = 2,
    /// <summary>Direct3D 9 API.</summary>
    Direct3D9 = 3
}
