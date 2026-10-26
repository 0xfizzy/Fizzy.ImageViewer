# Frames and pixels

[简体中文](../zh-CN/frame-pipeline.md)

Create an image from CPU memory, submit it, or read original pixels through a lease. Start with [a complete viewer](getting-started.md); the examples below assume that viewer remains alive.

## Choose a CPU frame format

[Getting started](getting-started.md) uses Gray8: one byte per pixel, with a default display range of 0–255. For other CPU images, use the same creation and submission API with a descriptor matching the source layout. The examples below cover the additional concerns of high-bit-depth grayscale and color with alpha; they assume a live `viewer` and the `Fizzy.ImageViewer` and `Fizzy.ImageViewer.Frames` namespaces. Check submission results as in the first example.

`FrameDescriptor(width, height, stride, format)` describes top-down, single-plane pixels. **Stride is bytes between row starts**, including padding, and must be positive and at least the packed row size. Supply at least `descriptor.RequiredBytes` bytes. Negative strides, planar images and Bayer data need conversion in the consumer. `ImageFrame.Copy` finishes copying before returning, so the source array or camera buffer can then be reused.

### High-bit-depth grayscale: set the display range

Gray16 uses two little-endian bytes per pixel. For a sensor storing 12-bit values in 16-bit words, set the display range to 0–4095 before the first submission. This changes display contrast without changing original query or raw export values; the default is 0–65535.

```csharp
ushort[] values = { 0, 1024, 2048, 4095 };
var pixels = new byte[values.Length * 2];
for (int i = 0; i < values.Length; i++)
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
        pixels.AsSpan(i * 2, 2), values[i]);
viewer.SetDisplayRange(new Fizzy.ImageViewer.Imaging.GrayDisplayRange(0, 4095));
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(4, 1, 8, FramePixelFormat.Gray16), pixels));
```

If the camera already supplies little-endian bytes with a valid stride, pass those bytes directly instead of repacking each value. Packed 10/12-bit camera formats must first be unpacked. Gray32Float uses four little-endian IEEE 754 bytes per pixel and defaults to a display range of 0–1; set the range in original-value units for temperature or depth data.

Use `SetDisplayRange` to accept a mapping and `DisplayFailed` to observe rendering failures. Pass `null` to restore format defaults.

### Color: match channel order and alpha

Choose the format matching the source bytes. For example, this Bgra32 frame contains a half-transparent red pixel followed by an opaque green pixel:

```csharp
byte[] pixels = { 0, 0, 255, 128,  0, 255, 0, 255 };
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(2, 1, 8, FramePixelFormat.Bgra32), pixels));
```

Rgb24/Bgr24 use three bytes per pixel in the named channel order; Bgr32 uses four, with padding rather than alpha in the fourth byte. Bgra32/Rgba32 use straight alpha. Pbgra32 stores colors already multiplied by alpha: the half-transparent red above would be `{ 0, 0, 128, 128 }`. Do not label straight-alpha bytes as premultiplied or premultiply them twice.

The following sections define storage lifetime, display and original-pixel query contracts.

## Transfer owned CPU memory

Use `Copy` for a borrowed camera buffer that the SDK will overwrite. Use `TakeOwnership` only when you control the storage until the final frame reference is released. It avoids the input copy; it does **not** eliminate CPU display preparation or WPF bitmap writes.

```csharp
var descriptor = new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8);
var pixels = new byte[descriptor.RequiredBytes];
Array.Fill(pixels, (byte)128); // Finish all writes before transfer.
var frame = ImageFrame.TakeOwnership(descriptor, pixels, static () => { });
var result = await viewer.SubmitFrameAsync(frame);
// Never write pixels again. This managed array needs no external release action.
```

For an existing memory-pool or camera-buffer owner, pass its exact memory slice and a release action that returns that buffer. Do not dispose the owner immediately after submission or after receiving `Accepted`: the current image, queries and independent leases may still retain it. A frozen viewer can retain a frame indefinitely. `TakeOwnership` calls the supplied release action once at final release, including construction validation failure; do not return the buffer again in a catch/finally block. Keep release short, nonthrowing and safe on the final releasing thread, which may be the Viewer STA. If the producer cannot meet these retention and threading requirements, use `Copy`.

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

For static XAML images, `ImageBindingSource.SetFrame` retains its own independent reference
without consuming the supplied `ImageFrame` or copying pixels. Each bound control acquires a
separate reference into this same pipeline; immutable storage and final-release rules remain
unchanged. Source updates and disposal are restricted to the host UI thread. This is a binding
adapter, not another frame queue or a replacement for explicit streaming submissions.
See [binding lifecycle and results](embedding.md#bind-static-images).

`SubmitFrameAsync` immediately consumes ownership and completes when reception finishes, independently
of display. Results are `Accepted`, `Frozen`, `Cancelled`, or `Closed`; inspect `Error` for reception-owned
cleanup failures even on rejection. Cancellation is checked only before acceptance. An accepted frame
may be replaced without ever being displayed; no terminal display task or replacement event is retained
for each input. FrameId is viewer-local and monotonic.

The pipeline keeps one in-flight frame, one newest waiting frame and the last committed frame.
A hidden, unloaded or minimized host accepts input but starts no conversion or upload. Restoring visibility
actively displays the latest waiting frame even if no further input arrives. In-flight conversion may
finish; a newly hidden host defers its UI commit and discards prepared pixels. Showing again may repeat
that conversion. No occlusion, screen-position, activation or Tab-specific detection is performed.

Successful UI commits update the current frame and mapping, then invoke `OnCommitted` before
`FrameCommitted`; neither proves monitor presentation. The callback lease is borrowed until return.
Subscribe before sending frames; retain independent leases and immutable callback metadata for deferred
work. `DisplayFailed` reports preparation or presentation errors on the owner STA with frame identity,
requested range and display version. It does not change a completed receipt. Failures are not automatically
retried; submit a new frame or explicitly set the range to request work again. A failed native presenter
retains its existing terminal safety contract. Subscriber exceptions are isolated; see
[event subscriptions](public-api.md#event-subscriptions).

Queries, measurements and snapshots use the last successfully committed frame and mapping, including
while hidden; before the first display commit there is no current frame. They do not read the waiting
input. Drawing updates and explicit queries are not suspended by the image pipeline.
Replaced or processed accepted-frame cleanup failures belong to the pipeline's shared disposal result,
not the newer receipt. If shutdown prevents delivery of a display failure, disposal retains it.

The CPU presenter owns two reusable WriteableBitmaps. Conversion runs off the UI thread;
an undisplayed bitmap is written before switching the source. WPF-native 8-bit formats keep
their native layout; mapped grayscale and display snapshots use Pbgra32.
Only the CPU presenter knows about mutable WPF bitmaps. GPU surface submissions use the D3DImage presenter described below.
Image coordinates are source pixels, independent of DPI; WPF still handles pan/zoom/overlays.
The image origin is its top-left boundary; integer pixel indices are distinct from
continuous positions. Pixel `(x, y)` has center `(x+0.5, y+0.5)`; use
`PixelCoordinate.ToImageCenter()` to obtain it. See [drawing coordinates](drawing-layers.md#coordinates-size-and-input)
for adapting algorithm results. The viewport requests WPF high-quality bitmap
scaling to reduce loss of small features when shrinking images. Display resampling
does not change original pixels or query results, and is not a subpixel accuracy guarantee.

`DisplayRange` is the latest requested grayscale mapping. `viewer.SetDisplayRange(range)` accepts
configuration on the owner STA and schedules display; it does not wait for a UI commit. Hidden changes
coalesce to the newest range. Defaults are Gray8 0–255, Gray16 0–65535 and Gray32Float 0–1.
Bounds must be finite and increasing; values are clipped, NaN/negative infinity render black and positive
infinity white. A non-null range on the current GPU frame is rejected before changing the request;
a waiting GPU frame with an incompatible range fails during display. Each request advances the display
version. An in-flight frame may commit an earlier mapping before a redraw applies the latest request.
Snapshots use only the last successfully committed range/version. A failure leaves the requested value
readable and is reported by `DisplayFailed`.

## Pixel access and measurements

Original-pixel access belongs to `Fizzy.ImageViewer.Frames`, alongside storage and leases.
Query and measurement results identify their original frame through `SourceFrame`; region pixels use region-local coordinates.

It includes `IFramePixelSource`, `CpuFramePixelReader`, pixel coordinates/regions, samples,
statistics and read results. `Fizzy.ImageViewer.Imaging` consumes these contracts for
display conversion, line sampling and query scheduling; Frames has no Imaging dependency.

`AcquireCurrentFrame()` returns an owned lease. `TryGetCpuPixels` exposes only existing
CPU memory and returns false for GPU storage. There is no general `Data` accessor.
`CpuFramePixelReader.TryReadPixel` and `ReadPixels` are static CPU-only decoding helpers; they never download GPU pixels.
Its `TryReadPixel` returns false for an out-of-bounds coordinate; an in-bounds GPU read throws
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

Capture, encoding and ownership contracts are in [snapshots](snapshots.md). Export retains the committed mapping, never an uncommitted display-range request.

## Shutdown and integrations

Stop and await the producer before awaiting Viewer disposal; never block the Viewer STA. Admitted submissions drain through final release and public result completion, including replacement cleanup on submitting threads. Independent leases can outlive the Viewer. See [Viewer shutdown](public-api.md#threads-and-window-lifetime) for the shared completion and failure contract.

Application adapters convert their own images to `ImageFrame`. Use `Copy` for borrowed CPU storage, `TakeOwnership` for immutable owned storage, and `D3D9ImageFrame.TakeOwnership` for ready GPU surfaces. Conversion, device compatibility and adapter tests belong to the consumer.

## Submit GPU frames

GPU frames use the same `ImageFrame` submission API. They require producer integration; a CPU array or arbitrary native pointer is not a GPU frame.

### Ready D3D9 surface

This adapter accepts resources already created and synchronized by the producer:

```csharp
static ValueTask<FrameSubmitResult> SubmitSurfaceAsync(
    Viewer viewer, FrameDescriptor originalDescriptor, nint readySurface,
    IFramePixelSource originalPixels, Action releaseResources)
{
    var frame = D3D9ImageFrame.TakeOwnership(
        originalDescriptor, readySurface, originalPixels, releaseResources);
    return viewer.SubmitFrameAsync(frame);
}
```

Supply a WPF-compatible, non-multisampled `IDirect3DSurface9` render target in `D3DPOOL_DEFAULT`, using premultiplied `D3DFMT_A8R8G8B8` pixels. Finish GPU writes and release compute mappings before transfer. The descriptor and `IFramePixelSource` describe original pixels; the surface must match their standard display conversion. Keep the device, surface and query resources valid and immutable until `releaseResources` runs. Ownership transfers even on factory failure. See the [GPU contract](frame-pipeline.md#gpu-surfaces-and-explicit-pixel-queries) before implementing an adapter.

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
