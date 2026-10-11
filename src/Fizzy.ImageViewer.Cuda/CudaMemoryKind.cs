namespace Fizzy.ImageViewer.Cuda;

/// <summary>Scratch storage required by an operation.</summary>
public enum CudaMemoryKind
{
    /// <summary>CUDA device memory.</summary>
    Device,
    /// <summary>Page-locked host memory suitable for asynchronous CUDA copies.</summary>
    PinnedHost
}
