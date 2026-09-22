# Validation

Windows and .NET 8+ SDK are required. Only this repository is needed; no cameras or other
devices are required. Run the commands from the repository root, sequentially because the
projects share the library's build outputs. These commands restore packages automatically.

```powershell
dotnet build Fizzy.ImageViewer.csproj -c Release
dotnet test tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj -c Release
dotnet build tools/FrameBenchmark/FrameBenchmark.csproj -c Release
dotnet build tools/DrawingBenchmark/DrawingBenchmark.csproj -c Release
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
visual ownership, reentrant registration/disposal, viewer closure, editor registry
removal and the absence of standalone overlay interaction.

`MeasurementSchedulerTests` use a manual clock and execution/publication queues for
batching, independent rates, expiration, failed queries, stale geometry,
re-registration and cancellation ownership. `MeasurementInteractionTests` cover
corner crossing, model/query/export consistency, label updates, invalid editing,
mode interruption, preview cancellation and reentrant removal. `LineStrengthTests`
and `QuerySchedulingTests` cover plot-window ownership and slow in-flight queries
on real STA dispatchers. See [measurement contracts](measurements.md).
Mouse capture state transitions use an injected capture boundary. Actual pointer
capture and dragging on an interactive desktop still require manual validation.

`ViewerTests` also check that asynchronous disposal cannot be vetoed by window
closing handlers, rejects subsequent API calls and consumes submissions with a
`Closed` result. `DrawingTests` check layer and drawing-handle invalidation and
that clearing business layers retains HUD text.
