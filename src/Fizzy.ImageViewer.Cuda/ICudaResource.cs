namespace Fizzy.ImageViewer.Cuda;

/// <summary>An external resource whose cleanup is owned by a bridge after registration.</summary>
public interface ICudaResource
{
    /// <summary>Releases the resource after proven completion and final token release.
    /// Invoke once. Failure leaves the remaining native state intact for permanent isolation;
    /// the bridge must retain this object and its context and expose the error.</summary>
    void Release();
}
