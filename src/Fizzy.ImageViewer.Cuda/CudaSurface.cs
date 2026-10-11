namespace Fizzy.ImageViewer.Cuda;

internal sealed class CudaSurface(nint owner, nint surface, Action<nint> release) : ICudaResource
{
    internal nint Owner { get; } = owner;
    internal nint Surface { get; } = surface;
    public void Release() => release(Owner);
}
