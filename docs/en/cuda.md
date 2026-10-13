# CUDA integration

[简体中文](../zh-CN/cuda.md)

Use the optional `Fizzy.ImageViewer.Cuda` package for CUDA-resident Gray8, Bgr24, Rgb24,
Bgra32, Pbgra32 and Rgba32 images. It supplies WPF-compatible display conversion and original
pixel queries. Consumers implement a small execution bridge; neither the core viewer nor the
extension depends on a particular application or compute runtime.

## Create a frame

Implement `ICudaFrameSource` for one independently retained, immutable source image. Its
`Descriptor` describes original pixels. Pass that source to the factory:

```csharp
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Cuda;

ImageFrame frame = await CudaImageFrame.TakeOwnershipAsync(source, cancellationToken);
var result = await viewer.SubmitFrameAsync(frame);
if (result.Error is { } error) throw error;
```

The factory immediately owns a nonnull source, including on validation failure or cancellation.
It returns an `ImageFrame` with its own `IFramePixelSource`; consumers do not construct a second
query adapter. Submission transfers the frame as usual. The final frame/lease release releases
the source and the independent display resource token. Independent leases can outlive Viewer;
the bridge's execution service must remain usable until those leases and their reads end.

## Execution bridge

`ICudaFrameSource.ExecuteAsync` invokes the synchronous callback exactly once, off the viewer
STA, after establishing source readiness. It must support concurrent calls or serialize its
backend. Do not retain or reenter the callback. A callback borrows an `ICudaExecutionScope`:

| Member | Consumer responsibility |
| --- | --- |
| Context / Stream | Standard CUDA handles, not runtime-private object handles; keep the context valid through external resource cleanup |
| InputPointer / InputByteLength / RowStride | Register read-only input use before exporting the device address; storage covers all referenced rows |
| AllocateScratch | Supply device or pinned host memory owned by this operation, retained through safe completion |
| RegisterResource | Register an `ICudaResource` before GPU access and return an independent ownership token |
| WaitForGpuCompletion | Prove submitted access ended without cancellation; do not end the operation |

The source descriptor's stride need not match a backend's device pitch; the scope provides the
actual device row stride. Both layouts must have the same dimensions, format and pixel values.
The extension validates dimensions, supported formats and accessible extent, not arbitrary
pointer validity. CPU input, negative strides, multi-plane storage and foreign opaque handles
are not accepted.

The frame factory immediately takes ownership. In contrast, `RegisterResource` transfers cleanup responsibility only on success; on failure the registering caller still owns cleanup.

The bridge owns the operation's final completion and retirement. A resource remains retained
by both the operation and the returned token. Only final safe release calls `ICudaResource.Release`.
If that cleanup fails, preserve the resource and context, report the failure through the runtime's
failure channel and quarantine; never retry destructive cleanup. A failed native call or wait
is not proof that device access ended. The bridge must drain or isolate all registered inputs,
scratch and external resources before allowing them to be reclaimed.

Cancellation before execution avoids GPU work. After enqueue, cancellation stops delivery but
never bypasses completion. Query host bytes are read only after a successful scope wait and
copied into independent results before scope exit. Surface publication also waits for the bridge's
final `ExecuteAsync` completion. Consumer exceptions propagate unchanged; extension native
failures use `CudaException` with a typed `CudaErrorDomain` and the original native code. Operation and owned cleanup failures
are preserved together.

## Formats and transfer costs

Gray8 expands to opaque BGRA. Bgr24/Rgb24 preserve original channels and add opaque alpha.
Bgra32 and Rgba32 preserve straight-alpha original values; display premultiplies each color as
`(color * alpha + 127) / 255`, with channel reordering for RGBA. Pbgra32 is already premultiplied:
its display path copies directly into the mapped texture without a conversion kernel or temporary
BGRA buffer. Original samples/statistics never unpremultiply. Raw TIFF preserves original values;
Display snapshots use the same standard mapping as the display surface.

Display does not read the image back to CPU, but still copies within device memory. Other formats
also use a conversion buffer. Each surface is separately allocated and immutable; there is no
texture pool. D3D9Ex devices are shared by active surfaces in the same CUDA context. Device/adapter
compatibility and CUDA/D3D9 interoperation remain required.

Queries download requested samples, compact count/min/max/sum statistics (32 bytes per semantic
channel), or the specified region. Gather splits at 65,536 points while preserving input order.
Statistics reorder source channels into Gray or R/G/B[/A]; small ROIs use only the needed reduction
blocks, capped at 256. Full snapshots explicitly request the full image. No implicit full-image
cache or download fallback exists. `CudaDiagnostics.QueryMetrics` counts extension transfers and
active query callbacks, not runtime allocations or quarantined bytes.
`EnqueuedQueries` and `EnqueuedResultBytes` count native calls that returned successfully; each gather
chunk counts separately, and partial enqueue before failure is not fully counted. `ActiveQueries` and
`ActiveResultBytes` cover query execution and managed result copying; zero is not proof of GPU
completion or empty quarantine. `GetLiveSurfaceCount()` loads the
native DLL and includes retained native surface owners.

No high-bit-depth CUDA mapping, premultiplied RGBA, Bayer conversion, custom LUT/range, D3D11/12
input, software fallback or device reconstruction is provided. WPF attachment and restoration
follow the [frame contract](frame-pipeline.md#gpu-surfaces-and-explicit-pixel-queries).

## Build and deployment

The core WPF project needs no CUDA toolchain. The optional managed project also builds without
initializing CUDA. The CUDA package includes `Fizzy.ImageViewer.Cuda.Native.dll` under
`runtimes/win-x64/native`; an incompatible native ABI fails explicitly. Native ABI 1 is declared
in `native/Cuda/imageviewer_cuda.h`; it uses only standard CUDA handles and caller-owned storage.

Native builds require Windows x64, CUDA Toolkit 11.8 and MSVC v142. The default is SM 7.5 with
PTX; select architectures appropriate to deployment. No external runtime SDK or sibling checkout
is required:

```powershell
./scripts/Build-Cuda.ps1 -Configuration Release -Architectures '75'
dotnet build src/Fizzy.ImageViewer.Cuda -c Release
dotnet pack src/Fizzy.ImageViewer.Cuda -c Release -o artifacts/packages
```

Native outputs go to `build/Release/native`. Packing without the native DLL fails; the core
package contains neither CUDA binaries nor CUDA source. Runtime execution requires a compatible
NVIDIA driver. No dependency is downloaded or compiled at application startup.

The build script compiles native tests without executing them. After separate hardware authorization,
run `ctest --test-dir build/obj/cuda -C Release --output-on-failure -L hardware` to check surface
pixels, six formats, alpha, padded pitch, gather and ROI boundaries. This does not establish physical
presentation latency, remote desktop, adapter migration or driver-reset recovery.
