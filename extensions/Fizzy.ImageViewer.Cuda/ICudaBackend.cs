using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Cuda;

// Native boundary injection is internal: the public extension always uses the same implementation.
internal interface ICudaBackend
{
    CudaSurface CreateSurface(nint context, int width, int height);
    void WriteSurface(CudaSurface surface, ICudaExecutionScope scope, FrameDescriptor descriptor, int format);
    byte[] Query(ICudaExecutionScope scope, FrameDescriptor descriptor, int kind, int[]? points, PixelRegion region);
}
