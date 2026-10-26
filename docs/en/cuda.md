# CUDA integration

[简体中文](../zh-CN/cuda.md)

Use one optional `Fizzy.ImageViewer.Cuda11`, `.Cuda12` or `.Cuda13` package for CUDA-resident Gray8, Bgr24, Rgb24,
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

Choose exactly one complete package: `Fizzy.ImageViewer.Cuda11`,
`Fizzy.ImageViewer.Cuda12`, or `Fizzy.ImageViewer.Cuda13`, matching the host's CUDA
major version. All three use the same `Fizzy.ImageViewer.Cuda` namespace and assembly.
Do not combine variants, including through transitive dependencies, or combine a variant
with the former `Fizzy.ImageViewer.Cuda` package. The build rejects these combinations.
Previously published packages remain immutable; new releases use the explicit names.

| Package | Build Toolkit | Host compiler | Default architecture |
| --- | --- | --- | --- |
| Fizzy.ImageViewer.Cuda11 | 11.8.0 | MSVC 14.38 (v143) | SM 7.5 |
| Fizzy.ImageViewer.Cuda12 | 12.6.3 | MSVC 14.38 (v143) | SM 7.5 |
| Fizzy.ImageViewer.Cuda13 | 13.0.2 | MSVC 14.38 (v143) | SM 7.5 |

These are build baselines, not certification of every minor version, GPU or execution
feature. Native ABI 1 uses standard CUDA handles and caller-owned storage. The host
must preserve its context, stream, memory and completion guarantees. A compatible
NVIDIA driver, GPU and CUDA/D3D9Ex adapter remain required. Static CUDA and MSVC
runtimes mean package consumers do not need the build Toolkit or Visual Studio.
No dependency is downloaded or compiled at application startup.

```powershell
dotnet add package Fizzy.ImageViewer.Cuda11
```

Ordinary managed development needs no CUDA toolchain. For local native development,
keep one Toolkit (11.8 by default). Install only VS 2022 Build Tools with MSVC
14.38 and Windows SDK 10.0.22621.0; the full IDE and v142 are unnecessary.
Run the installer option from an elevated PowerShell when those components are missing:

```powershell
./scripts/Setup-Cuda.ps1 -InstallBuildTools
./scripts/Test-Cuda.ps1 -CudaMajor 11 -Configuration Release
# Explicit hardware execution; requires a suitable local GPU and desktop environment.
./scripts/Test-Cuda.ps1 -CudaMajor 11 -Configuration Release -RunHardwareTests
```

Setup prepares pinned CMake 3.31.8 and Ninja 1.12.1 under `artifacts/cuda/tools`.
It does not change the NVIDIA driver or persistent environment variables. Build scripts
select MSVC 14.38 in a child process and use Ninja without CUDA Visual Studio integration.
Use `-CudaRoot` to select an existing Toolkit. CI additionally uses
`Setup-Cuda.ps1 -CudaMajor 12 -DownloadToolkit -InstallBuildTools` to fetch only
SHA-256-verified NVIDIA compiler components. The shared matrix is
`eng/cuda-matrix.json`. Release builds require its exact versions.

Native outputs live under `build/<Configuration>/native/cuda<major>/`; intermediates
under `build/obj/cuda/<major>/<Configuration>/`; native tests under
`build/<Configuration>/tests/Cuda<major>/`. Reports and temporary packages go to
`artifacts/cuda/<major>/`. Native DLLs carry a separate build record identifying source,
compiler and checksum. Packaging rejects missing, stale, mismatched or modified inputs:

```powershell
./scripts/Build-Cuda.ps1 -CudaMajor 11 -Configuration Release
dotnet pack src/Fizzy.ImageViewer.Cuda -c Release -p:CudaMajor=11 -o artifacts/packages
```

GitHub Actions compiles all three variants, compiles native tests, runs managed CUDA
tests on .NET 8/10, and verifies package contents and consumer publishing. Standard
Windows runners do not execute GPU tests. A successful matrix therefore establishes
build and package compatibility, not CUDA 12/13 hardware or physical presentation.
Local hardware mode runs bounded native tests for six formats, alpha, padded pitch,
gather, ROI, statistics, D3D9Ex surface readback and cleanup. Readback is not a test of
physical screen presentation. Requested hardware tests never silently skip on an
unsuitable environment. See [release preparation and recovery](releasing.md#optional-cuda-package).
