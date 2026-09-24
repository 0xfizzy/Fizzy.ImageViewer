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
format changes and TIFF readback. LibTIFF is a test-only dependency used as an independent
decoder for the built-in TIFF writer. TIFF tests cover all pixel formats, strip layouts,
raw floating-point bits, alpha tags, size limits, cancellation and failed replacement cleanup.
Passing the suite does not verify physical screen presentation, mixed-DPI monitors, remote
desktop or real hardware. These require a separate interactive check.
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

`UnifiedMeasurementTests` compare built-in and custom ROI notifications, labels and
statistics, verify immutable retained pixel/profile results, reject stale in-flight ROI
results after editing/removal, and exercise circle edit writeback and callback removal.
Query failures, invalid combinations and geometry validation also have regression coverage.

`MeasurementOwnershipTests` cover model-owned custom-tool cleanup, cancellation exceptions,
visual ownership, reentrant registration/disposal, viewer closure, unsupported editors,
rectangle drag sessions and the absence of standalone overlay interaction.

`MeasurementNotificationsTests` verify the public change stream for built-in and custom ROI,
retained geometry/result snapshots, STA callbacks, subscriber isolation, editing invalidation
and reentrant removal. Session ownership tests verify that interrupted or ended contexts
cannot attach previews to replacement sessions, and unrelated items survive session cleanup.
Measurement cleanup is also checked after presentation detachment.

`MeasurementReentryTests` cover session replacement from click, move, cancellation,
tool switching and entry into editing, with and without callback exceptions. They
check active tool, interaction mode, cursor, input suppression and preview ownership,
including restart during measurement disposal and rejection of new sessions during closure.
`MeasurementInteractionTests` also cover restarting the same built-in tool from a
completion subscriber. Run the focused suite with:

```powershell
dotnet test tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj -c Release --filter FullyQualifiedName~MeasurementReentryTests
```

`PixelQuerySchedulerTests` use a manual clock and execution/publication queues for
batching, independent rates, expiration, failed queries, stale geometry,
re-registration and cancellation ownership. `MeasurementInteractionTests` cover
corner crossing, model/query/export consistency, label updates, invalid editing,
mode interruption, preview cancellation and reentrant removal. They also verify worker
geometry updates refresh every supported shape's editing controls before public notifications,
and that external or reentrant updates during a rectangle drag rebase all four corners. `LineProfileTests`
and `QuerySchedulingTests` cover plot-window ownership and slow in-flight queries
on real STA dispatchers. See [measurement contracts](measurements.md).
`ViewportPanTests` verify failed/lost capture, restart, screen-coordinate deltas and
reentrant release through the same capture boundary used by measurement editing. Actual pointer
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
that clearing business layers retains HUD text. Drawing tests also exercise both `Add`
overloads and single/collection/empty replacement through `DrawingHandle`, preserving
visual identity, stacking order and click identity. Invalid replacements retain prior
content; all image-coordinate `Draw*` helpers support replacement from worker threads
and reject updates after shutdown.

`ViewerTests` cover pending versus committed display settings, redraw without new-frame
notifications, menu Closed-before-Click and rapid reopening, independently retained menu
targets after shutdown, and exactly-once STA presenter disposal. `SnapshotCaptureTests`
exercise serialized export reads, cancellation while queued, read failures, and lease
release independently of a window or a native GPU surface.
`FramePresentationTests` verify that GPU preparation performs no pixel reads and rejects
CPU display mapping; they do not bind a native surface or validate real CPU/GPU switching.

`FrameMetadataTests` cover standalone queries, cropped/raw/display pixel descriptors,
local coordinates and explicit source provenance. `MeasurementHandleTests` cover worker
updates/completion/disposal, STA callbacks, immutable retained snapshots, cancelled
previews and handles after shutdown. `MenuExecutionTests` cover awaitable actions,
reopening while an action is pending, independent actions, revocation and failures
after the viewer dispatcher exits.

## Architectural boundaries

`MeasurementStyleTests` exercise independent STA viewers, brush snapshots and per-item
selection colors. `ViewerInitializationTests` inject startup/cleanup failures and verify
frame/measurement/presenter release, original exceptions and actual STA exit. `LayerInteractionTests`
cover routed selection input, one cancellation per layer operation and complete bulk cleanup
after failures. `PixelInfoControllerTests` run HUD sampling without a Viewer or measurement
context. MeasurementGeometryTests verify normalized bounds, endpoint order, zero extents and invalid
coordinates/radii. ViewerTests verify submission callbacks and public frame-notification
order despite callback failures. The public API baseline test detects exported type/member changes; its update
procedure is documented in [public API](public-api.md).

Initialization tests also verify public hidden creation, configuration and frame submission
before showing, disposal without showing, and shared cleanup after window closure.
Partial composition failures after window, pipeline and measurement
creation verify exactly-once presenter cleanup on the STA, original exception preservation
and actual thread exit even when presenter disposal throws.
Style tests cover caller-owned `Tag` data, per-visual zoom dimensions,
point fill selection and editing after metadata changes. Menu tests cover visibility,
check state, separator normalization and captured targets across closure/reopening.
Menu lifetime tests cover independent registrations, cross-thread and reentrant revocation,
retained click invalidation, delayed cleanup after Closed, failed-opening unfreeze, and
binding cleanup on shutdown and initialization failure. Initialization checkpoints include
fully composed menus. Edit tests retain model writeback and drag-start corner behavior
without an editor factory or forwarding wrapper. Unregistered visuals cannot replace a
selected measurement or become edit targets; retained menu actions ignore disposed targets.
Measurement resource failure tests verify visual detachment and idempotent disposal
even when specialized cleanup throws.

`PixelFormatContractTests` verify statistics for all supported formats using explicit source
bytes and expected semantic values, including Bgr32 padding, RGB/BGR order and preserved
premultiplied channels. They also check invalid statistics channel counts and unknown formats.
`TiffSnapshotWriterTests` independently verify encoded channel layout, bit depth and alpha tags.

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

## Tool sessions and layer ownership

`MeasurementSessionTests` verify independent state when a registration is shared across two
viewers, factory reentry and failure cleanup, and rejection of ended creation contexts.
They also clear model-owned content and query subscriptions without an interaction coordinator,
and after coordinator disposal. Existing reentry tests cover interrupted clicks, moves,
cancellation, completion and editing using explicit session contexts.
Test source files are grouped by capability (Measurements, Interaction, Layers, Drawing, Frames, Rendering, Imaging,
Viewport, Hud, Menus, Snapshots, Viewer and Api); test filters continue to use the same namespaces.

`LineSamplingTests` verify clipping and endpoint order without presentation state.
`LineProfileTests` cover data-only results without plot windows and visual cleanup after
collection callback failures. `ViewerLifetimeTests` verify STA removal dispatch, retained
handles after shutdown and preservation of callback failures when shutdown starts reentrantly.

`ToolSessionLifetimeTests` cover exactly-once STA session disposal on completion, cancellation,
callback failure, shutdown and factory supersession, including replacement from a throwing
disposal callback. `QueryResultSnapshotTests` verify independent immutable query collections.
Public API/menu tests verify pixel HUD configuration before display and shared menu state.

`MeasurementNotificationsTests` also verify two-subscriber reentry across viewer and handle
notifications, result invalidation ordering and closure from a subscriber, including terminal
removal before `Closed`. `MeasurementModelBoundaryTests` cover typed result payload ownership
and original-operation failures combined with attachment/completion cleanup failures.

`MeasurementQueryOptionsTests` check the geometry/query matrix against actual requests and result provenance, and reject invalid window/query combinations. Profile tests verify independent data-only and window-enabled queries and window ownership. Tool protocol tests cover registration replacement, context-scoped asynchronous
finishing, origin retention and original callback errors combined with cleanup failures.

`PublicApiTests` exercise borrowed window, frame, drawing, HUD, query and measurement
capabilities against one Viewer, including frame consumption after closure. Facade coverage
includes inherited interfaces; the API baseline records each capability and its signatures.
Boundary tests reserve global layer management for `IViewer`, verify that drawing and
measurement capabilities share their respective live layer handles, and exercise independent
layer clearing, measurement cancellation and global clearing through those public contracts.
