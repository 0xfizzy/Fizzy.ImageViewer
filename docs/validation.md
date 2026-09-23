# Validation

Windows and .NET 8+ SDK are required. Only this repository is needed; no cameras or other
devices are required. Run the commands from the repository root, sequentially because the
projects share the library's build outputs. These commands restore packages automatically.

```powershell
dotnet build Fizzy.ImageViewer.csproj -c Release
dotnet test tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj -c Release
dotnet build tools/FrameBenchmark/FrameBenchmark.csproj -c Release
dotnet build tools/DrawingBenchmark/DrawingBenchmark.csproj -c Release
dotnet build tools/ResizeBenchmark/ResizeBenchmark.csproj -c Release
dotnet build tools/LineProfileBenchmark/LineProfileBenchmark.csproj -c Release
dotnet pack Fizzy.ImageViewer.csproj -c Release -o artifacts
```

The library suite uses independent hidden STA windows, injected presenters and explicit gates
for queue tests. It exercises ownership, pixels, submission/notification order, frozen snapshots,
format changes and TIFF readback. Passing it does not verify physical screen presentation,
mixed-DPI monitors, remote desktop or real hardware. These require a separate interactive check.
No package release is performed by these validation commands.
The Windows CI workflow runs restore, build, test, tool builds and pack on pushes and pull
requests, using only this repository's checkout. It does not publish packages.

Synthetic benchmark (hidden windows, commit timings, no hardware):

```powershell
dotnet run --project tools/FrameBenchmark -c Release -- artifacts/frame-benchmark.json
```

It covers 1080p/4K, Gray8/Bgr24, 30/60 input FPS, one/two viewers and line sampling on/off.
The direct WritePixels reference measures only the old copy operation, not an old application
build. Neither benchmark measures physical presentation or chart-window rendering. Allocation
counts include the harness; private-memory deltas include pooling, JIT and GC effects.

Drawing benchmark (writes CSV measurements to standard output):

```powershell
dotnet run --project tools/DrawingBenchmark -c Release
```

## Pixel queries

PixelQueryTests cover GPU ownership during async reads, CPU stride/finite statistics, explicit ROI
snapshots and query options. QuerySchedulingTests inject slow sources to verify merged sampling,
STA responsiveness, replacement, geometry invalidation, expiration and shutdown ownership.
GPU-backed producers should validate their native surface and pixel-source implementation in
the consumer repository. This library does not require a specific GPU runtime or device SDK.

## Measurement ownership and interaction

`MeasurementScopeTests` cover custom-tool cleanup, cancellation exceptions,
visual ownership, reentrant registration/disposal, viewer closure, unsupported editors,
rectangle drag sessions and the absence of standalone overlay interaction.

`MeasurementReentryTests` cover session replacement from click, move, cancellation,
tool switching and entry into editing, with and without callback exceptions. They
check active tool, interaction mode, cursor, input suppression and preview ownership,
including restart during scope disposal and rejection of new sessions during closure.
`MeasurementInteractionTests` also cover restarting the same built-in tool from a
completion subscriber. Run the focused suite with:

```powershell
dotnet test tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj -c Release --filter FullyQualifiedName~MeasurementReentryTests
```

`PixelQuerySchedulerTests` use a manual clock and execution/publication queues for
batching, independent rates, expiration, failed queries, stale geometry,
re-registration and cancellation ownership. `MeasurementInteractionTests` cover
corner crossing, model/query/export consistency, label updates, invalid editing,
mode interruption, preview cancellation and reentrant removal. `LineStrengthTests`
and `QuerySchedulingTests` cover plot-window ownership and slow in-flight queries
on real STA dispatchers. See [measurement contracts](measurements.md).
Mouse capture state transitions use an injected capture boundary. Actual pointer
capture and dragging on an interactive desktop still require manual validation.

For interactive validation, cancel a preview with Escape, start consecutive
measurements from a completion callback, and switch between point, line and ROI
tools. Confirm that the pen cursor and suppressed marker input belong only to the
active measurement session. Hide the measurement layer during a preview and check
that the preview is removed and normal input returns; show it again and start a new
measurement. Close the viewer during a preview and with a completed line-profile
window open, then confirm both windows close and `DisposeAsync` completes. These
checks use synthetic frames and do not require camera or motion hardware.

Pixel HUD cases in `PixelQuerySchedulerTests` cover the completion-based 10 Hz
cap, lower configured rates, moving results, 300 ms retention, failures, expiration,
session changes and stationary video updates without real-clock sleeps.
`PixelInfoStateTests` cover coordinate/value pairing, immediate invalid-target
hiding and stable invariant columns for gray, floating-point and premultiplied data.

`ViewerTests` also check that asynchronous disposal cannot be vetoed by window
closing handlers, rejects subsequent API calls and consumes submissions with a
`Closed` result. `DrawingTests` check layer and drawing-handle invalidation and
that clearing business layers retains HUD text.

`ViewerTests` cover pending versus committed display settings, redraw without new-frame
notifications, menu Closed-before-Click and rapid reopening, independently retained menu
targets after shutdown, and exactly-once STA presenter disposal. `SnapshotCaptureTests`
exercise serialized export reads, cancellation while queued, read failures, and lease
release independently of a window or a native GPU surface.
`FramePresentationTests` verify that GPU preparation performs no pixel reads and rejects
CPU display mapping; they do not bind a native surface or validate real CPU/GPU switching.

## Architectural boundaries

`MeasurementStyleTests` exercise independent STA viewers, brush snapshots and per-item
selection colors. `ViewerInitializationTests` inject startup/cleanup failures and verify
frame/scope/presenter release, original exceptions and actual STA exit. `LayerInteractionTests`
cover routed selection input, one cancellation per layer operation and complete bulk cleanup
after failures. `PixelInfoOverlayTests` run HUD sampling without a Viewer or measurement
context. The public API baseline test detects exported type/member changes; its update
procedure is documented in [public API](public-api.md).

Initialization tests also verify that rollback and window closure bypass overridden
disposal methods. Style tests cover caller-owned `Tag` data, per-visual zoom dimensions,
point fill selection and editing after metadata changes. Menu tests cover visibility,
check state, separator normalization and captured targets across closure/reopening.
Measurement resource failure tests verify visual detachment and idempotent disposal
even when specialized cleanup throws.

## Line-profile allocations

```powershell
dotnet run --project tools/LineProfileBenchmark -c Release
```

The STA benchmark warms buffers, then reports managed bytes and time per update
for 1024/4096 samples in gray/RGB. It separates data updates, changed/unchanged
WPF drawing commands and offscreen `RenderTargetBitmap` rendering. One red sample
changes per update; RGB changes rebuild all channels to preserve shared scaling.
Drawing measurements include the harness drawing context; they exclude sampling,
window composition and native allocations. They do not establish a zero-GC frame pipeline.
