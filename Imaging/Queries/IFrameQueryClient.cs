using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging.Queries;

internal interface IFrameQueryClient
{
    QueryPolicy Policy => default;
    QueryRequest? Capture(FrameDescriptor descriptor);
    void ClearResult();
    void InvalidateResult(ResultInvalidation reason) => ClearResult();
}
