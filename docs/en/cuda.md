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

| Package | Build Toolkit | Host compiler | Architecture coverage |
| --- | --- | --- | --- |
| Fizzy.ImageViewer.Cuda11 | 11.8.0 | MSVC 14.38 (v143) | Common Pascal and newer GPUs |
| Fizzy.ImageViewer.Cuda12 | 12.6.3 | MSVC 14.38 (v143) | Common Pascal and newer GPUs |
| Fizzy.ImageViewer.Cuda13 | 13.0.2 | MSVC 14.38 (v143) | Common Turing and newer GPUs |

Coverage is defined by architecture and required features, not a GPU model whitelist
or a requirement to test each model. Newer GPUs can use compatible machine code or
driver JIT compilation of the embedded PTX from an older Toolkit; their SM number need
not appear individually in the package. Applications do not need to configure PTX.
This forward compatibility does not promise every behavior of unknown future hardware.

Compute capability coverage is separate from display support: display requires a usable
Windows CUDA/D3D9Ex interoperation environment with a compatible GPU/adapter and NVIDIA
driver. The driver must support the package's CUDA runtime and any required PTX JIT.
Native ABI 1 uses standard CUDA handles and caller-owned storage. The host must preserve
its context, stream, memory and completion guarantees. Static CUDA and MSVC
runtimes mean package consumers do not need the build Toolkit or Visual Studio.
No dependency is downloaded or compiled at application startup.

```powershell
dotnet add package Fizzy.ImageViewer.Cuda11
```

Ordinary managed development needs no CUDA toolchain. For local native development,
keep one Toolkit (11.8 by default) and an MSVC toolset supported by that Toolkit.
The setup script installs the CI baseline: VS 2022 Build Tools with MSVC 14.38
and Windows SDK 10.0.22621.0; the full IDE and v142 are unnecessary.
The installer also selects the standard MSVC x64/x86 tools component because nvcc
requires its `vcvarsall.bat` installation layout, even when the environment is loaded
through `VsDevCmd.bat`. This brings in the VS 2022 default toolset as an installation
dependency; local builds select the newest installed compatible toolset.
Run the installer option from an elevated PowerShell when those components are missing:

```powershell
./scripts/Setup-Cuda.ps1 -InstallBuildTools
./scripts/Test-Cuda.ps1 -CudaMajor 11 -Configuration Release
# Validate the official architecture coverage on a suitable local GPU/desktop.
./scripts/Test-Cuda.ps1 -CudaMajor 11 -Configuration Release -FullMatrix -RunHardwareTests
```

Setup prepares pinned CMake 3.31.8 and Ninja 1.12.1 under `artifacts/cuda/tools`.
It does not change the NVIDIA driver or persistent environment variables. Build scripts
select a compatible MSVC in a child process and use Ninja without CUDA Visual Studio integration.
Use `-MsvcVersion 14.38` on `Build-Cuda.ps1` or `Test-Cuda.ps1` to select a toolset
explicitly, or omit it for automatic selection. The Toolkit's compiler checks remain
enabled: a stable C ABI does not make unsupported host compilers valid for nvcc.
For example, CUDA 11.8 accepts MSVC 19.1x–19.3x, but rejects 19.4x.
Build records capture the actual compiler version; CI and release builds pin 14.38.
Use `-CudaRoot` to select an existing Toolkit. CI additionally uses
`Setup-Cuda.ps1 -CudaMajor 12 -DownloadToolkit -InstallBuildTools` to fetch only
SHA-256-verified NVIDIA compiler components. The shared matrix is
`eng/cuda-matrix.json`. Release builds require its exact versions.

Local builds default to the architectures reported by `nvidia-smi` for local GPUs,
with native machine code only (for example, T600 selects `75-real`). Use
`-Architectures '75-real;86-real'` to select explicit CMake CUDA targets, or `-FullMatrix`
to build the official list without a GPU. If no GPU can be identified, an explicit
selection is required; no architecture is guessed. nvcc rejects unsupported targets.
CI and `-Release` always use the full matrix and reject conflicting overrides.

The matrix uses CMake `CUDA_ARCHITECTURES` notation: CUDA 11.8/12.6 include
`60-real;61-real;70-real;75-real;80-real;86-real;89-real;90-real;90-virtual`;
CUDA 13.0 includes `75-real;80-real;86-real;89-real;90-real;100-real;120-real;120-virtual`.
Common targets carry native code and the highest target also carries PTX for forward
compatibility. Architecture-specific `a`/`f` targets are not used.

Native outputs live under `build/<Configuration>/native/cuda<major>/`; intermediates
under `build/obj/cuda/<major>/<Configuration>/`; native tests under
`build/<Configuration>/tests/Cuda<major>/`. Reports and temporary packages go to
`artifacts/cuda/<major>/`. Native DLLs carry a separate build record identifying source,
compiler, requested architectures, binary inspection and checksum. Every build uses
the selected Toolkit's `cuobjdump` to check native code and PTX for both kernel modules
in the final DLL. `architectures.*` preserves the inspection evidence; `build.json`
also records DLL bytes and configure/build duration (incremental and clean times must
not be compared). Packaging rejects missing, stale, mismatched or modified inputs;
release sealing additionally rejects local single-architecture artifacts:

```powershell
./scripts/Build-Cuda.ps1 -CudaMajor 11 -Configuration Release
dotnet pack src/Fizzy.ImageViewer.Cuda -c Release -p:CudaMajor=11 -o artifacts/packages
```

GitHub Actions compiles the complete architecture lists with all three pinned Toolkits,
checks the DLL contents, compiles native tests, runs managed CUDA
tests on .NET 8/10, and verifies package contents and consumer publishing. Standard
Windows runners do not execute GPU tests. A successful matrix therefore establishes
build and package compatibility, not CUDA 12/13 hardware or physical presentation.
Local hardware mode runs bounded native tests for six formats, alpha, padded pitch,
gather, ROI, statistics, D3D9Ex surface readback and cleanup. Readback is not a test of
physical screen presentation. Requested hardware tests never silently skip on an
unsuitable environment. See [release preparation and recovery](releasing.md#optional-cuda-package).
