using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Cuda;

/// <summary>An independently retained, immutable CUDA image and its executable read access.</summary>
public interface ICudaFrameSource : IDisposable
{
    /// <summary>Original pixel layout. Supported formats are Gray8, Bgr24, Rgb24, Bgra32, Pbgra32 and Rgba32.</summary>
    FrameDescriptor Descriptor { get; }

    /// <summary>Runs the callback exactly once in a ready read scope, off the viewer STA.
    /// Calls may overlap; serialize backend access when necessary. Never retain or reenter the callback.</summary>
    /// <remarks>The source and its execution service remain usable until Dispose. The bridge owns submission,
    /// completion and retirement. Cancellation after enqueue must drain access or quarantine all dependencies
    /// before returning. Failure is not proof of safe release. Preserve operation and cleanup errors.</remarks>
    ValueTask ExecuteAsync(Action<ICudaExecutionScope> action, CancellationToken cancellationToken);
}
