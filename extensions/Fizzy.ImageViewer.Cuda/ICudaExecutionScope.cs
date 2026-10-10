namespace Fizzy.ImageViewer.Cuda;

/// <summary>A synchronous, thread-affine borrowed CUDA operation. Handles and scratch never escape it.</summary>
public interface ICudaExecutionScope
{
    /// <summary>Standard CUcontext handle; its lifetime includes registered resource cleanup.</summary>
    nint Context { get; }
    /// <summary>Standard CUDA stream handle. Zero denotes the context's default stream.</summary>
    nint Stream { get; }
    /// <summary>Registered read-only device address of the first input pixel.</summary>
    nint InputPointer { get; }
    /// <summary>Accessible bytes starting at InputPointer, including row padding where present.</summary>
    long InputByteLength { get; }
    /// <summary>Positive distance between device row starts; may differ from the source descriptor's stride.</summary>
    long RowStride { get; }
    /// <summary>Allocates operation-owned device or pinned host scratch. Retain it until safe completion,
    /// including on exceptions and cancellation. Returns a nonzero address for positive byte counts.</summary>
    nint AllocateScratch(long byteCount, CudaMemoryKind kind);
    /// <summary>Registers a resource before GPU access and returns an independent ownership token.
    /// Success transfers cleanup to the bridge; failure leaves cleanup with the caller.</summary>
    /// <remarks>The operation and token each retain the resource. Release it only after both have ended
    /// safely. If Release fails, preserve the resource and context, report the failure through the runtime's
    /// failure channel and quarantine; never retry destructive cleanup. The token releases its own reference once.</remarks>
    IDisposable RegisterResource(ICudaResource resource);
    /// <summary>Synchronously proves this operation's submitted CUDA accesses have ended.
    /// Does not finish or release the operation. Must not observe user cancellation instead of completion.
    /// A thrown error is not safety proof; the bridge still owns drain/quarantine.</summary>
    void WaitForGpuCompletion();
}
