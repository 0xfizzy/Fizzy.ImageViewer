# Frame pipeline

[简体中文](zh-CN/frame-pipeline.md)

## Input and lifetime

`ImageFrame.Copy(descriptor, bytes)` copies synchronously; the caller keeps its storage.
`ImageFrame.TakeOwnership(descriptor, memory, release)` takes immutable storage and calls
`release` exactly once when the final frame/lease is disposed, including validation failure.
If construction and release both fail, an `AggregateException` preserves the construction
error first and the release error second. Release is not retried.
Width/height must be positive, stride must be positive and at least the valid row size.
Row padding and non-contiguous source rows are supported. Data is top-down, single plane;
Gray16 and Gray32Float are little endian. Unsupported formats fail explicitly.
Storage size differs from semantic channel count: Gray16 uses two bytes for one channel,
and Bgr32 uses four bytes for three channels; its fourth byte is padding, not alpha.
Bgra32 and Pbgra32 have four semantic channels, with straight and premultiplied alpha,
respectively.

`SubmitFrameAsync` consumes the frame even when cancelled, frozen or closed. Never dispose
or mutate transferred storage. `ImageFrame` cannot be submitted twice. Each `FrameLease`
is an independent ownership token with idempotent disposal. Acquire a separate lease before
starting asynchronous work. Do not dispose a lease concurrently with reading its memory.
External release callbacks should not throw. A submission's own release failure is returned
in `FrameSubmitResult.Error`; release failures for viewer-owned current or redraw leases
are reported by `DisposeAsync`. Independent lease disposal propagates its release failure.
Repeated disposal does not retry a release callback.

Release callbacks execute synchronously on whichever thread releases the final
reference: a submitting thread, rendering/query worker, or the Viewer STA. There is
no dedicated release thread. Producers must satisfy their own thread-affinity
requirements and keep synchronous release short; blocking it also blocks that thread.

The viewer keeps the current original frame for sampling. Acquiring a snapshot/measurement
can extend this lifetime. A frozen viewer can hold a frame indefinitely. Camera pools must
allow retention, or their adapters must make a copy. Read-only access does not prevent other
owners from mutating memory: immutability is part of the producer contract.

## Submission and display

Each viewer has an independent STA and one background rendering loop. At most one submission
is processing and one waits. New submissions replace the waiting frame (`Superseded`).
Results are `Committed`, `Superseded`, `Frozen`, `Cancelled`, `Closed`, or `Failed`.
`Status` describes the submission outcome; `Error` can also contain a cleanup failure for
a dropped submission. Inspect both fields. A cleanup failure does not undo a commit or
change a drop reason; when processing and cleanup both fail, both errors are retained.
Cancellation is best effort until the UI commit boundary; cancellation after commit does not
undo it. A request's token is observed while processing and before commit. A waiting cancelled
frame terminates when dequeued/replaced/frozen/closed rather than using a per-frame callback.

`Committed` means the source and current frame have changed on the UI thread; it does not mean
the monitor displayed it. FrameId is viewer-local and monotonic. Notifications occur after
writing pixels. The per-submission `OnCommitted` runs first, then `FrameCommitted`.
The `OnCommitted` frame lease is borrowed for that callback only.
Measurement query notifications are scheduled independently. Callbacks must not block on another viewer
submission; use asynchronous work with an acquired lease. Subscriber failures are isolated.

The CPU presenter owns two reusable WriteableBitmaps. Conversion runs off the UI thread;
an undisplayed bitmap is written before switching the source. WPF-native 8-bit formats keep
their native layout; mapped grayscale and display snapshots use Pbgra32.
Only the CPU presenter knows about mutable WPF bitmaps. GPU surface submissions use the D3DImage presenter described below.
Image coordinates are source pixels, independent of DPI; WPF still handles pan/zoom/overlays.

`DisplayRange` is the latest requested grayscale mapping. Use
`await viewer.SetDisplayRangeAsync(range)` to change it and observe redraw failures.
Defaults: Gray8 0–255, Gray16 0–65535, Gray32Float 0–1.
Finite increasing bounds are required. Values are clipped; NaN/negative infinity render black,
positive infinity white. Range changes schedule a redraw with a separate display version.
When a current frame exists, the returned task completes after a redraw or submission
commits that version; rendering failures fault the task. A newer range request or
viewer closure cancels an outstanding update. With no current frame, the call only
configures future submissions and completes immediately. A non-null range for a
current GPU frame is rejected before changing the request. Do not synchronously wait
for an update on the Viewer STA.
An already preparing first submission can still commit the mapping it captured
before that configuration call; await the first submission before requesting a redraw.
Snapshot capture uses the last committed range/version, not a range still awaiting
redraw or one whose update failed. A failed update leaves the requested value readable;
call `SetDisplayRangeAsync` explicitly to request another update.

## Pixel access and measurements

Original-pixel access belongs to `Fizzy.ImageViewer.Frames`, alongside storage and leases.
Query and measurement results identify their original frame through `SourceFrame`; region pixels use region-local coordinates.

It includes `IFramePixelSource`, `CpuFramePixelReader`, pixel coordinates/regions, samples,
statistics and read results. `Fizzy.ImageViewer.Imaging` consumes these contracts for
display conversion, line sampling and query scheduling; Frames has no Imaging dependency.

`AcquireCurrentFrame()` returns an owned lease. `TryGetCpuPixels` exposes only existing
CPU memory and returns false for GPU storage. There is no general `Data` accessor.
`CpuFramePixelReader.TryReadPixel` and `ReadPixels` are static CPU-only decoding helpers; they never download GPU pixels.
Its `TryRead` returns false for an out-of-bounds coordinate; an in-bounds GPU read throws
`NotSupportedException`. Use lease asynchronous reads for backend-independent access.

Use `FrameLease.ReadPixelsAsync(coordinates, token)`, `ComputeRegionStatisticsAsync(region,
token)` and `ReadRegionAsync(region, token)` for backend-independent access. Coordinates use
`ReadOnlyMemory<PixelCoordinate>` and results preserve input order, raw units and format.
Each call acquires its own lease before asynchronous work and retains storage through completion.
The CPU backend can complete its calculation synchronously on the calling thread;
these methods do not promise worker dispatch. The viewer scheduler and snapshot
capture provide their own off-STA execution. Direct callers needing responsiveness
must choose an appropriate execution thread.
Operation and owned-resource cleanup failures are preserved together, with the
operation failure first. Region ownership is handed to the caller only after source
lease cleanup succeeds; an undeliverable region is disposed by the operation.
Pixel and statistics results carry the queried lease's `FrameInfo`. `PixelSamplesResult`
and `RegionStatistics` copy constructor inputs and expose read-only collections that
can be retained and shared. Pixel providers transfer their returned sample arrays to the
caller and must not reuse or mutate them after returning. `FrameLease.Info.Descriptor`
always describes its own pixels. Unsubmitted frames, including derived pixel buffers, have
FrameId 0 and no source timestamp. Submission assigns a viewer-local ID to the submitted lease;
leases acquired before submission remain unsubmitted.

`RegionPixels.SourceFrame` and `ImageSnapshot.SourceFrame` identify their input frame.
Their `Region` is expressed in that input frame's coordinates. `AcquirePixels()` returns an
independent lease describing the cropped or converted pixels, without copying source metadata
onto it. Queries on that lease use local coordinates and describe the derived pixels; retain
the containing region/snapshot when source provenance is needed. `RegionPixels` owns its
independent CPU image and must be disposed. It is created by `FrameLease.ReadRegionAsync`
after validating the provider result; consumers cannot construct it directly.
Unsupported capabilities fail without download fallback.

CPU formats remain Gray8/Gray16/Gray32Float/Rgb24/Bgr24/Bgr32/Bgra32/Pbgra32/Rgba32. Statistics return
count/min/max/mean per semantic channel (Gray or R/G/B[/A]); non-finite floating values are ignored,
with zero count and null statistics when no finite values exist. Premultiplied channels stay premultiplied.
`RegionStatistics.Channels` uses Gray, R/G/B or R/G/B/A order regardless of source byte order;
Bgr32 padding is excluded. Construction rejects unknown formats and incorrect channel counts.
External `IFramePixelSource` implementations must follow that ordering and return the source
frame's format; frame leases reject a mismatched format. Display ranges do not constrain
original query values, including Gray32Float values outside the default 0–1 display range.

One pixel query scheduler per viewer captures geometry on STA and queries off STA, with at most
one batch in flight. Each batch executes and publishes either one ROI or a shared gather
for due mouse and line coordinates. Oldest-due selection serves pending work fairly;
unselected requests retain no frame lease and capture fresh geometry/frame on a later tick.
Slow ROI queries therefore do not age one another or delay publication of a completed gather.
Point and line coordinates use the half-open pixel coverage rectangle
`[0, Width) × [0, Height)`: pixel `(x, y)` covers `[x, x+1) × [y, y+1)`.
Lines are clipped to that coverage, their endpoints are floored to pixel indices,
and Bresenham samples retain the input direction and both endpoint pixels. Very large finite
coordinates use exact clipping of the supplied binary values to avoid losing the pixel interval;
this cannot recover precision already lost before calling the API. A crossing
endpoint on the outer right/bottom boundary is clamped to the last covered pixel;
a line lying on or only touching those exclusive edges has no samples.
ROI geometry uses floor(left/top), ceil(right/bottom),
then image intersection; empty regions are not queried. Shape editors preserve statistics labels.
The separate interaction coordinator manages selection, editing and creation sessions.
See [measurement ownership and interaction](measurements.md) for geometry and extension contracts.

`Viewer.QueryOptions` accepts `PixelQueryOptions`: PixelQueryRateHz=30, LineQueryRateHz=30, RegionQueryRateHz=10 Hz,
MaxResultAge=100 ms, all positive. Each rate must produce a representable interval of at
least one `TimeSpan` tick and less than `TimeSpan.MaxValue`; invalid settings are rejected
when assigned. Due requests are served in due order; pending geometry is replaced
by its latest state without resetting the execution interval, including during edits and resizes.
Monotonic age starts at the frame/geometry snapshot, not camera capture time.
Geometry changes, deletion, disabling and closing invalidate old work. Results may describe a
recent prior frame within the age limit; advancing video clears expired values, while an unchanged
paused frame keeps its valid result. FrameId stays in API results/logging and is never a HUD label.
`QueryMetrics.Batches` counts completed backend batches, including batches containing
failed queries; it is not a successful-query or publication count. `ExpiredResults`
counts individual completed results rejected at publication for exceeding `MaxResultAge`,
not already displayed values later cleared by a scheduler tick.
`LastDurationMilliseconds` spans frame/geometry capture through backend completion,
including worker scheduling but excluding the wait for STA publication and its callbacks.

The bottom-left pixel HUD has its own cap of `min(10, PixelQueryRateHz)` Hz, with a full
interval after each completed query (including failures). Mouse moves coalesce to
the latest target without resetting that interval. It may publish a recently sampled
position from the same interaction session; the coordinates and value always update
together. Other measurements still require the exact geometry version.
While waiting, the HUD retains the complete previous result for up to 300 ms from
its sampling start if the target or frame has changed, then shows `—`. An unchanged
static result does not expire. Failures show `—` immediately; `MaxResultAge` still
controls whether a newly completed query can publish. Leaving the image, disabling
the HUD or losing the frame hides it and prevents old work from reappearing.
The non-interactive HUD has no background and uses invariant numeric formatting
with a minimum of seven characters per value. Longer values remain complete;
narrow viewports clip the HUD rather than wrapping or resizing.
Each target capture recomputes the image coordinate from the current container
pointer and viewport transform, including when fit, layout or image dimensions change
without mouse movement. Hidden/unloaded viewports and pointers outside the container
have no query target.

## Freeze, snapshot and save

Opening the context menu freezes the current frame, clears pending input and invalidates
in-flight pre-freeze submissions. Closing it resumes new input. A menu-scoped lease pins the
frame and display settings through Click/Closed ordering; saving acquires its own lease before
the file dialog. The menu lease is released when input drains or on viewer shutdown.

`CaptureSnapshotAsync(Raw|Display)` captures the current committed frame at call time and
returns independent immutable storage. `Raw` preserves input values; `Display` applies the
committed display mapping without annotations, HUD or viewport scaling.
The overload accepting PixelRegion reads only that region. Snapshots record `SourceFrame` and
region origin. Full snapshots explicitly request the entire region; GPU results are never cached
back into source storage. One export read per Viewer executes at a time, with at most one
additional capture admitted to wait. Further calls throw `InvalidOperationException`
synchronously before acquiring a frame lease. Accepted captures retain the frame current
at the call, including while waiting. Completion, cancellation or failure frees the slot.
The ROI save menu pins both frame and integer region before opening the file dialog.
Dispose snapshots when finished. A save retains its own lease until completion.
Raw capture takes ownership of the independently read CPU region without copying it
again. That region's release callback runs when the snapshot's final pixel reference
is released, including independent pixel leases retained after snapshot disposal.
Capture cleans up a candidate snapshot if another owned resource fails to
release before delivery; operation and cleanup failures are preserved together.

`ImageSnapshot.SaveAsync(path, encoding, token)` writes a temporary file in the destination
directory and replaces the target only after successful encoding. Cancellation is checked
before replacement and between TIFF rows; WPF PNG/JPEG/BMP encoding itself is not interruptible.
Display exports support PNG, JPEG, BMP and TIFF. JPEG composites transparency onto black.
Raw exports require TIFF and never silently quantize high-bit-depth values.
Encoding/cancellation and cleanup failures are preserved together, with the operation failure
first. Cleanup can fail after the target was successfully replaced; a failed save task does
not by itself prove that the destination is unchanged.
The context menu's **Save Image As...** action selects display PNG/JPEG/BMP or raw TIFF
through the file dialog's file type. **Save Region Image As...** offers the same choices
when a completed rectangular measurement is selected.

TIFF uses uncompressed Classic TIFF, UINT Gray16 or IEEEFP Gray32Float, top-left orientation,
RGB channel order and associated/unassociated alpha tags. Bgr32 padding is omitted. Images
exceeding the conservative Classic TIFF size bound are rejected; BigTIFF is not implemented.
The context menu allows one save at a time. Independently created snapshot objects may be saved
concurrently by application code. Captured snapshots remain usable after closing the viewer.

## Shutdown and integrations

Use `await viewer.DisposeAsync()` or `await using` to terminate queued/rendering and
measurement work without blocking the STA. `Viewer` and `IViewer` implement
`IAsyncDisposable`. `Closed` reports window closure; it does not signal that background
rendering, measurement queries or the window thread have finished. Await `DisposeAsync()`
to wait for those operations to finish and release their frame leases.
Once closed the Viewer cannot be reopened.

Disposal is idempotent and waits for rendering, in-flight measurement queries and
the window thread to finish. Submission calls admitted before shutdown are also
drained through final release and public result completion, including calls replaced
on another caller's thread or dropped while frozen/cancelled. Pixel sources that ignore cancellation can delay this
completion until their operations release their leases. Programmatic closure is
not blocked by `CanUserClose` or a cancelling window-closing handler.
Repeated calls share the same completion and failure result. Owned-resource cleanup
failures are reported after the remaining safe cleanup and actual STA exit; notification
subscriber failures remain isolated and logged. Failure does not prove all resources were
released. Resources that cannot be safely detached must not be reused.
Cleanup failures from internally owned query leases and menu snapshot pins are retained
until shutdown, even if later queries or menu sessions succeed. Ordinary query/provider
failures remain query failures and do not become shutdown failures.

Once disposal begins, viewer properties, window commands, measurement registration,
frame acquisition, snapshot capture and layer operations reject new work with
`ObjectDisposedException`. Frame submission retains its ownership contract and
returns `Closed`. Previously acquired leases and snapshots remain independently
owned; drawing and HUD handles are invalidated during cleanup.

Application integrations should adapt their own frame and image types to `ImageFrame`.
Use `Copy` for borrowed storage, `TakeOwnership` to transfer immutable CPU storage, or
`D3D9ImageFrame.TakeOwnership` to transfer a ready GPU surface. Producers own conversion, lifetime and
device compatibility; integration tests for their adapters belong in the consumer repository.

## GPU surfaces and explicit pixel queries

`D3D9ImageFrame.TakeOwnership(descriptor, surface, pixelSource, release)` transfers a ready,
immutable BGRA `IDirect3DSurface9` and its producer-owned lifetime. The producer must supply
matching dimensions, a WPF-compatible D3D9 render target, and raw pixels consistent with the
standard display conversion. The surface cannot be overwritten while any lease exists.

The external-surface entry is a trusted native boundary: a nonzero pointer is not proof of
compatibility. Supply a valid, non-multisampled `D3DFMT_A8R8G8B8` render target in
`D3DPOOL_DEFAULT`, with premultiplied alpha and dimensions matching the descriptor.
Use a WPF-compatible device (the provided CUDA component uses D3D9Ex with
`D3DCREATE_MULTITHREADED` and `D3DCREATE_FPU_PRESERVE`). The producer owns device compatibility,
native metadata validation and GPU synchronization. Finish all writes and relinquish compute
mappings before transfer; merely enqueueing GPU work is insufficient. Keep the device,
surface and original query resources valid until the final release callback. The descriptor
describes original pixels, not the BGRA surface's byte layout. No custom color mapping,
orientation change or display range is represented by this API; Display snapshots reconstruct
the standard display conversion from original pixels rather than reading the surface.

GPU submissions share the same latest-frame scheduler, FrameId, freeze/cancellation semantics
and commit callbacks. D3DImage is created and bound on the viewer STA; no DisplayConverter or
CPU pixel provider is invoked on the normal GPU display path. Front-buffer restoration rebinds
the retained current surface. CPU submissions switch back to WriteableBitmap.

`IFramePixelSource` supplies asynchronous gather, statistics and region reads independently of
the display surface. The library has no CUDA dependency. The producer retains its source image
and query resources until the final frame lease is released. Query failures affect interaction,
not display, and must never trigger an implicit full-image readback. Cancellation of submitted
GPU work must retain buffers and leases until device completion, even when publication is cancelled.

Region reads transfer an independently owned CPU `ImageFrame` with the requested dimensions
and source format. Coordinates in that image start at the region's origin; `FrameLease`
adds source provenance in `RegionPixels`. A provider must finish access to source resources
before its operation returns or throws, including cancellation. The frame's release callback
owns the provider's resources; the library does not separately dispose the provider.
This completion rule applies to every provider method. Independent leases, direct reads,
the query scheduler and snapshot capture can call the same provider concurrently, including
different methods. The provider must support that concurrency or serialize its own backend
access. The scheduler's single batch and the export gate do not serialize all provider calls.

DisplayRange is unsupported for GPU surfaces; producers must apply their GPU mapping before
submission. Consumers are responsible for converting source formats to the supported frame contract.
Surface loss due to a driver reset is distinct from WPF front-buffer availability and is not
recovered by recreating the producer's device here. No software rendering fallback is enabled.
If a WPF front-buffer restoration callback fails, the presenter stops accepting GPU surfaces.
The presentation owner retains that error for `Viewer.DisposeAsync()`, including after switching
to CPU display; it does not throw the error out of the WPF event callback or retry restoration.
This asynchronous failure does not change a previously returned submission result.
If surface attachment fails, presentation stops using that presenter. A successful detach
permits lease release; if detachment cannot be confirmed, the presenter and potentially
attached leases are retained for process lifetime. This deliberate isolation prevents
native use after release and is reported as a failure, not automatic recovery.

The optional [CUDA component](cuda.md) supplies native display conversion and original-pixel
queries for Gray8/Bgr24/Rgb24/Bgra32/Pbgra32/Rgba32. Application adapters still supply their source-frame and runtime
integration; the WPF assembly itself has no CUDA dependency.

## Internal composition

`ViewerHost` assembles components and owns STA startup and shutdown. `Viewer` forwards
public API calls and raises notifications. It does not own rendering queue or menu-target state.
`FramePipeline` and presentation implementations belong to Rendering; Frames contains
storage, leases and submission contracts.

| Component | State and responsibility |
| --- | --- |
| `FramePipeline` | Latest waiting submission, current frame, freeze epoch, requested/committed display settings, render completion and submission results |
| `FramePresentation` | Background pixel preparation, STA CPU/GPU upload and image attachment, backend resources and disposal |
| `MenuSnapshotSession` | Menu-owned frame and ROI, delayed target release, menu-save exclusion |
| `SnapshotCapture` | Serial region reads and independent Raw/Display snapshot generation |

`CommittedViewLease` owns a frame lease plus the range and version captured atomically
from the last committed display. Crop regions are separate values; export synchronization
belongs to `SnapshotCapture`, not to the lease. Menu save targets acquire independent leases
before showing a file dialog.

The pipeline shares `ViewerLifetime.Gate` for submission, publication and shutdown admission.
Expensive upload runs outside that gate; image attachment and current-frame publication run
inside it. Commit, freeze/resume and presentation disposal are serialized by the STA.
The commit callback invokes the submission callback and then the public frame event;
display-only redraws do not invoke it. Measurements and the pixel HUD independently
subscribe to the shared query scheduler, which samples acquired committed frames.

Shutdown stops the pipeline, releases the menu session, closes interaction/measurement/layer
resources and disposes presentation on STA. Asynchronous disposal then waits for rendering,
query completion and the window thread. Already acquired export work retains its own
lease and export gate and may finish after the Viewer closes.
