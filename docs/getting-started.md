# Getting started

[简体中文](zh-CN/getting-started.md)

This guide starts with a single image, introduces representative CPU frames and GPU entry points, then explains how to feed a live viewer efficiently.

## Create a viewer

Use Windows and .NET 8 with a `net8.0-windows` project and `<UseWPF>true</UseWPF>`. Install the package:

```powershell
dotnet add package Fizzy.ImageViewer
```

Viewer creates its own STA; a console application can use it without creating an application dispatcher. With implicit usings enabled, this is a complete console entry point:

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
viewer.Closed += (_, _) => closed.TrySetResult();
viewer.Title = "My first image";

var pixels = new byte[640 * 480];
Array.Fill(pixels, (byte)128);
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != FrameSubmitStatus.Committed)
    throw new InvalidOperationException($"Initial frame: {result.Status}");

viewer.Show();
await closed.Task;
// await using waits for shutdown and reports cleanup failures.
```

Configure and subscribe before `Show()`. Submission transfers ownership immediately, even when cancelled or closed: do not submit the same frame twice or dispose its transferred storage. `Committed` means the UI accepted the frame, not that the monitor has displayed it. Inspect both `Status` and `Error`, including for dropped frames.

`Closed` is a notification, not proof that rendering and queries have drained. Stop and await your producer before awaiting `DisposeAsync`; never synchronously wait for either from a Viewer callback. A Viewer has one window lifetime; create a new instance after closure.

## Choose a CPU frame format

The first example uses Gray8: one byte per pixel, with a default display range of 0–255. For other CPU images, use the same creation and submission API with a descriptor matching the source layout. The examples below cover the additional concerns of high-bit-depth grayscale and color with alpha; they assume the same `viewer` and namespaces. Check submission results as in the first example.

`FrameDescriptor(width, height, stride, format)` describes top-down, single-plane pixels. **Stride is bytes between row starts**, including padding, and must be positive and at least the packed row size. Supply at least `descriptor.RequiredBytes` bytes. Negative strides, planar images and Bayer data need conversion in the consumer. `ImageFrame.Copy` finishes copying before returning, so the source array or camera buffer can then be reused.

### High-bit-depth grayscale: set the display range

Gray16 uses two little-endian bytes per pixel. For a sensor storing 12-bit values in 16-bit words, set the display range to 0–4095 before the first submission. This changes display contrast without changing original query or raw export values; the default is 0–65535.

```csharp
ushort[] values = { 0, 1024, 2048, 4095 };
var pixels = new byte[values.Length * 2];
for (int i = 0; i < values.Length; i++)
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
        pixels.AsSpan(i * 2, 2), values[i]);
await viewer.SetDisplayRangeAsync(new Fizzy.ImageViewer.Imaging.GrayDisplayRange(0, 4095));
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(4, 1, 8, FramePixelFormat.Gray16), pixels));
```

If the camera already supplies little-endian bytes with a valid stride, pass those bytes directly instead of repacking each value. Packed 10/12-bit camera formats must first be unpacked. Gray32Float uses four little-endian IEEE 754 bytes per pixel and defaults to a display range of 0–1; set the range in original-value units for temperature or depth data.

To adjust an existing image, await its submission first, then await `SetDisplayRangeAsync` to observe redraw completion or failure. Pass `null` to restore format defaults.

### Color: match channel order and alpha

Choose the format matching the source bytes. For example, this Bgra32 frame contains a half-transparent red pixel followed by an opaque green pixel:

```csharp
byte[] pixels = { 0, 0, 255, 128,  0, 255, 0, 255 };
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(2, 1, 8, FramePixelFormat.Bgra32), pixels));
```

Rgb24/Bgr24 use three bytes per pixel in the named channel order; Bgr32 uses four, with padding rather than alpha in the fourth byte. Bgra32/Rgba32 use straight alpha. Pbgra32 stores colors already multiplied by alpha: the half-transparent red above would be `{ 0, 0, 128, 128 }`. Do not label straight-alpha bytes as premultiplied or premultiply them twice.

See the [frame pipeline](frame-pipeline.md) for complete format, display and original-pixel query contracts.

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

For an existing memory-pool or camera-buffer owner, pass its exact memory slice and a release action that returns that buffer. Do not dispose the owner immediately after submission or after awaiting `Committed`: the current image, queries and independent leases may still retain it. A frozen viewer can retain a frame indefinitely. `TakeOwnership` calls the supplied release action once at final release, including construction validation failure; do not return the buffer again in a catch/finally block. Keep release short, nonthrowing and safe on the final releasing thread, which may be the Viewer STA. If the producer cannot meet these retention and threading requirements, use `Copy`.

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

### CUDA source

With the optional `Fizzy.ImageViewer.Cuda` package and a consumer implementation of `ICudaFrameSource`, the extension creates the surface and pixel-query provider:

```csharp
static async Task<FrameSubmitResult> SubmitCudaAsync(
    Viewer viewer, Fizzy.ImageViewer.Cuda.ICudaFrameSource source,
    CancellationToken cancellationToken)
{
    var frame = await Fizzy.ImageViewer.Cuda.CudaImageFrame.TakeOwnershipAsync(
        source, cancellationToken);
    return await viewer.SubmitFrameAsync(frame, cancellationToken: cancellationToken);
}
```

The factory owns `source` immediately, including failure or cancellation. Supported inputs are Gray8, Rgb24, Bgr24, Bgra32, Pbgra32 and Rgba32. Display avoids CPU readback but still performs device-memory copies. GPU frames do not support `SetDisplayRangeAsync` with a non-null range. See [CUDA integration](cuda.md) for the execution bridge, deployment and device requirements.

## Use ImageViewer efficiently

### Bound work before submission

The viewer processes one submission and retains only the newest waiting frame. Replaced waiting frames complete as `Superseded`; it is a live display, not a lossless recording queue.

For a pull-based producer, await each submission before producing the next frame, as in [LiveOverlays](../samples/LiveOverlays/Program.cs). This limits producer work naturally. For a push camera callback, keep an application-owned bounded latest-frame handoff and a worker that observes every submission result. Release any owned frame discarded before submission. Avoid a growing `Task.Run` queue, unbounded channel or list of submission tasks: the viewer's queue cannot bound work allocated upstream. Stop admission and drain the producer during shutdown.

Awaiting a submission observes commit/drop and any release error for that submission; it does not mean the camera buffer is reusable. Only the final release callback establishes that for transferred storage. Track status counts and errors without logging every successful frame in a hot callback.

### Reduce copies and conversion

- Keep the source's supported format and stride. Do not expand Gray8 to BGRA or reorder RGB/BGR solely for display. Native 8-bit CPU layouts are preserved where supported; Gray16, Gray32Float, Rgba32 and remapped Gray8 require display conversion.
- Reuse a producer buffer with `Copy`, or transfer a separately owned immutable buffer with `TakeOwnership`. `Copy` already rents internal memory; add producer pooling only after measuring allocations and retention. Never refill transferred memory early.
- Reuse one Viewer for the window lifetime. Stable image dimensions and display format allow its two CPU WriteableBitmaps to be reused. Change display range when needed, rather than requesting a redraw every frame.
- For pixels already on the GPU, use a supported GPU adapter to avoid a full-image round trip. GPU integration is not automatically faster for CPU input and still has allocation, synchronization and copy costs.

### Batch overlays and limit query work

Create one drawing handle per independently updated group, retain it for the stream, and call `Replace` when results change:

```csharp
using var markers = viewer.Layers.Markers.Add(
    Array.Empty<Fizzy.ImageViewer.Drawing.DrawingElement>());
// In the update loop, replace the whole batch using current detection results.
markers.Replace(new Fizzy.ImageViewer.Drawing.DrawingElement[]
{
    new Fizzy.ImageViewer.Drawing.CircleElement(
        new System.Windows.Point(100, 100), 5, System.Windows.Media.Brushes.Red)
});
```

Thousands of individual `Add` calls create thousands of visuals and dispatches. One collection creates one visual; batch replacement still costs geometry construction and drawing. Throttle expensive detection/overlay updates independently of image rate. For frame-aligned overlays, follow the [live overlay protocol](drawing-layers.md#live-camera-overlays-consumer-requirements).

Pixel inspection, profiles and region statistics need not run at camera frame rate. For example, configure lighter query limits before showing the viewer:

```csharp
viewer.QueryOptions = new Fizzy.ImageViewer.Imaging.PixelQueryOptions
{
    PixelQueryRateHz = 15,
    LineQueryRateHz = 10,
    RegionQueryRateHz = 5,
    MaxResultAge = TimeSpan.FromMilliseconds(200)
};
```

These are application choices, not performance guarantees. The viewer shares one query batch at a time; the pixel HUD also caps itself at 10 Hz. `MaxResultAge` controls stale-result acceptance, not how quickly a query executes. Use `QueryMetrics` to inspect duration and expired results. Prefer point/ROI requests over full snapshots, especially for GPU sources. Dispose owned leases, regions and snapshots promptly; they retain memory and may extend a camera buffer's lifetime. Direct CPU query methods can run synchronously on the caller, so move expensive analysis off the UI thread.

### Keep callbacks short and measure the whole path

`OnCommitted` and `FrameCommitted` run on the Viewer STA. Do not perform encoding, device waits, heavy analysis, synchronous task waits or a submission-and-wait inside them. For asynchronous work, acquire an independent lease before the callback returns and release it when work finishes; the callback's borrowed lease cannot be retained as-is. Bound this background work too.

Measure producer conversion/copy cost, managed allocation, submission status counts, query duration and overlay update cost separately. Submission completion is not physical presentation latency. Run the [frame, drawing and query benchmarks](validation.md#line-profile-allocations) for the path you change, then validate interaction and display on the target machine. Synthetic benchmarks do not establish sustained camera throughput or mixed-DPI behavior.

## Complete examples

Run from the repository root. These manual checks open WPF windows:

```powershell
dotnet run --project samples/BasicViewer
dotnet run --project samples/LiveOverlays
dotnet run --project samples/MeasurementsAndExport
```

- [BasicViewer](../samples/BasicViewer/Program.cs): copied grayscale pixels and awaited closure.
- [LiveOverlays](../samples/LiveOverlays/Program.cs): simulated frames, reusable batch handle, production ending before disposal.
- [MeasurementsAndExport](../samples/MeasurementsAndExport/Program.cs): fresh-session point tool, events and PNG output under `artifacts/samples/`.

`./scripts/Test-Samples.ps1` builds without opening windows. Export accepts an optional output directory; choose one without a valuable existing `snapshot.png`.

Continue with [frames](frame-pipeline.md), [lifetime](public-api.md) and [measurements](measurements.md).
