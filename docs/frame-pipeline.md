# Frame pipeline

## Input and lifetime

`ImageFrame.Copy(descriptor, bytes)` copies synchronously; the caller keeps its storage.
`ImageFrame.TakeOwnership(descriptor, memory, release)` takes immutable storage and calls
`release` exactly once when the final frame/lease is disposed, including validation failure.
Width/height must be positive, stride must be positive and at least the valid row size.
Row padding and non-contiguous source rows are supported. Data is top-down, single plane;
Gray16 and Gray32Float are little endian. Unsupported formats fail explicitly.

`SubmitFrameAsync` consumes the frame even when cancelled, frozen or closed. Never dispose
or mutate transferred storage. `ImageFrame` cannot be submitted twice. Each `FrameLease`
is an independent ownership token with idempotent disposal. Acquire a separate lease before
starting asynchronous work. Do not dispose a lease concurrently with reading its memory.
External release callbacks should not throw; Viewer logs callback failures at its boundary.

The viewer keeps the current original frame for sampling. Acquiring a snapshot/measurement
can extend this lifetime. A frozen viewer can hold a frame indefinitely. Camera pools must
allow retention, or their adapters must make a copy. Read-only access does not prevent other
owners from mutating memory: immutability is part of the producer contract.

## Submission and display

Each viewer has an independent STA and one background rendering loop. At most one submission
is processing and one waits. New submissions replace the waiting frame (`Superseded`).
Results are `Committed`, `Superseded`, `Frozen`, `Cancelled`, `Closed`, or `Failed`.
Cancellation is best effort until the UI commit boundary; cancellation after commit does not
undo it. A request's token is observed while processing and before commit. A waiting cancelled
frame terminates when dequeued/replaced/frozen/closed rather than using a per-frame callback.

`Committed` means the source and current frame have changed on the UI thread; it does not mean
the monitor displayed it. FrameId is viewer-local and monotonic. Notifications occur after
writing pixels. The per-submission `OnCommitted` runs first, then measurement/public events.
Its frame lease is borrowed for the callback only. Callbacks must not block on another viewer
submission; use asynchronous work with an acquired lease. Subscriber failures are isolated.

The CPU presenter owns two reusable WriteableBitmaps. Conversion runs off the UI thread;
an undisplayed bitmap is written before switching the source. WPF-native 8-bit formats keep
their native layout; mapped grayscale and display snapshots use Pbgra32.
Only the CPU presenter knows about mutable WPF bitmaps. GPU surface submissions use the D3DImage presenter described below.
Image coordinates are source pixels, independent of DPI; WPF still handles pan/zoom/overlays.

`DisplayRange` maps grayscale only. Defaults: Gray8 0–255, Gray16 0–65535, Gray32Float 0–1.
Finite increasing bounds are required. Values are clipped; NaN/negative infinity render black,
positive infinity white. Range changes schedule a redraw with a separate display version.
Snapshot capture uses the last committed range/version, not a range still awaiting redraw.

## Pixel access and measurements

`AcquireCurrentFrame()` returns an owned lease. `TryGetCpuPixels` exposes only existing
CPU memory and returns false for GPU storage. There is no general `Data` accessor.
`FramePixelReader` is a CPU-only decoding helper; it never downloads GPU pixels.

Use `FrameLease.ReadPixelsAsync(coordinates, token)`, `ComputeRegionStatisticsAsync(region,
token)` and `ReadRegionAsync(region, token)` for backend-independent access. Coordinates use
`ReadOnlyMemory<PixelCoordinate>` and results preserve input order, raw units and format.
Each call acquires its own lease before asynchronous work and retains storage through completion.
Results carry source FrameInfo; RegionPixels additionally records the original region, owns an
independent CPU image and must be disposed. Unsupported capabilities fail without download fallback.

CPU formats remain Gray8/Gray16/Gray32Float/Rgb24/Bgr24/Bgr32/Bgra32/Pbgra32. Statistics return
count/min/max/mean per semantic channel (Gray or R/G/B[/A]); non-finite floating values are ignored,
with null statistics when no finite values exist. Premultiplied channels stay premultiplied.

One interaction coordinator per viewer captures geometry on STA and queries off STA, with at most
one batch in flight. Mouse and line coordinates share one gather. Line clipping, rounded endpoints,
Bresenham order and distances are unchanged. ROI geometry uses floor(left/top), ceil(right/bottom),
then image intersection; empty regions are not queried. Shape editors preserve statistics labels.

`Viewer.QueryOptions` accepts `PixelQueryOptions`: PixelRate=30, LineRate=30, RegionRate=10 Hz,
MaxResultAge=100 ms, all positive. Due requests are served in due order; pending geometry is replaced
by its latest state. Monotonic age starts at the frame/geometry snapshot, not camera capture time.
Geometry changes, deletion, disabling and closing invalidate old work. Results may describe a
recent prior frame within the age limit; advancing video clears expired values, while an unchanged
paused frame keeps its valid result. FrameId stays in API results/logging and is never a HUD label.
`QueryMetrics` reports completed batches, expired results and last completion duration.

## Freeze, snapshot and save

Opening the context menu freezes the current frame, clears pending input and invalidates
in-flight pre-freeze submissions. Closing it resumes new input. A menu-scoped lease pins the
frame and display settings through Click/Closed ordering; saving acquires its own lease before
the file dialog. The menu lease is released when input drains or on viewer shutdown.

`CaptureSnapshotAsync(Raw|Display)` captures the current committed frame at call time and
returns independent immutable storage. `Raw` preserves input values; `Display` applies the
committed display mapping without annotations, HUD or viewport scaling.
The overload accepting PixelRegion reads only that region. Snapshots record source FrameInfo and
region origin. Full snapshots explicitly request the entire region; GPU results are never cached
back into source storage. One export read per Viewer executes at a time.
The ROI save menu pins both frame and integer region before opening the file dialog.
Dispose snapshots when finished. A save retains its own lease until completion.

`ImageSnapshot.SaveAsync(path, encoding, token)` writes a temporary file in the destination
directory and replaces the target only after successful encoding. Cancellation is checked
before replacement and between TIFF rows; WPF PNG/JPEG/BMP encoding itself is not interruptible.
Display exports support PNG, JPEG, BMP and TIFF. JPEG composites transparency onto black.
Raw exports require TIFF and never silently quantize high-bit-depth values.

TIFF uses uncompressed Classic TIFF, UINT Gray16 or IEEEFP Gray32Float, top-left orientation,
RGB channel order and associated/unassociated alpha tags. Bgr32 padding is omitted. Images
exceeding the conservative Classic TIFF size bound are rejected; BigTIFF is not implemented.
The context menu allows one save at a time. Independently created snapshot objects may be saved
concurrently by application code. Captured snapshots remain usable after closing the viewer.

## Shutdown and integrations

Prefer `await viewer.DisposeAsync()` / `CloseAsync()`: these terminate queued/rendering and
measurement work without blocking the STA. Synchronous Dispose on that STA initiates closure;
off the STA it waits. Once closed the Viewer cannot be reopened.

Application integrations should adapt their own frame and image types to `ImageFrame`.
Use `Copy` for borrowed storage, `TakeOwnership` to transfer immutable CPU storage, or
`TakeD3D9Surface` to transfer a ready GPU surface. Producers own conversion, lifetime and
device compatibility; integration tests for their adapters belong in the consumer repository.

## GPU surfaces and explicit pixel queries

`ImageFrame.TakeD3D9Surface(descriptor, surface, pixelSource, release)` transfers a ready,
immutable BGRA `IDirect3DSurface9` and its producer-owned lifetime. The producer must supply
matching dimensions, a WPF-compatible D3D9 render target, and raw pixels consistent with the
standard display conversion. The surface cannot be overwritten while any lease exists.

GPU submissions share the same latest-frame scheduler, FrameId, freeze/cancellation semantics
and commit callbacks. D3DImage is created and bound on the viewer STA; no DisplayConverter or
CPU pixel provider is invoked on the normal GPU display path. Front-buffer restoration rebinds
the retained current surface. CPU submissions switch back to WriteableBitmap.

`IFramePixelSource` supplies asynchronous gather, statistics and region reads independently of
the display surface. The library has no CUDA dependency. The producer retains its source image
and query resources until the final frame lease is released. Query failures affect interaction,
not display, and must never trigger an implicit full-image readback. Cancellation of submitted
GPU work must retain buffers and leases until device completion, even when publication is cancelled.

DisplayRange is unsupported for GPU surfaces; producers must apply their GPU mapping before
submission. Consumers are responsible for converting source formats to the supported frame contract.
Surface loss due to a driver reset is distinct from WPF front-buffer availability and is not
recovered by recreating the producer's device here. No software rendering fallback is enabled.
