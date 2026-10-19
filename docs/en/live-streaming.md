# Live streaming

[简体中文](../zh-CN/live-streaming.md)

Integrate a camera or other continuous producer after completing [Getting started](getting-started.md). The viewer bounds its own frame queue; your application owns acquisition, upstream buffering, overlay scheduling, and producer shutdown.

Start with [LiveOverlays](../../samples/LiveOverlays/Program.cs), a simulated producer that reuses drawing handles and stops before viewer disposal.

## Bound work before submission

The viewer processes one display attempt and keeps only the newest waiting frame. `SubmitFrameAsync`
completes on reception (`Accepted`), not display; replaced input receives no later replacement result.
Hidden, unloaded and minimized viewers continue accepting frames and display only the latest on return.

A pull producer may await reception before producing again, but this does not pace acquisition to display.
Choose an explicit producer rate. For push callbacks, keep an application-owned bounded latest-frame
handoff and a worker observing receipt errors. Release owned frames discarded before submission. Avoid
unbounded channels, Task.Run queues and task lists; the viewer cannot bound upstream allocations.
Stop admission and drain the producer during shutdown.

Receipt completion does not make a camera buffer reusable; only its final release callback does.
Subscribe to `DisplayFailed` for later conversion/upload failures and use `FrameCommitted` or
`OnCommitted` for actual UI submission. Snapshot or measurement setup requiring pixels must wait for
that notification, not merely acceptance. Observe reception and display metrics separately.

## Live camera overlays: consumer requirements

Keep layers and batches alive for the display session. A camera frame is a data
update, not a reason to recreate the visual tree. Consumers of continuous camera
or detection streams must follow these rules:

| Avoid in the frame loop | Use instead |
| --- | --- |
| `Clear()` followed by `Add(...)`, or disposing and recreating batches | Create each batch once and call `Replace(...)` on it |
| Calling `DrawCircle`, `DrawLine`, etc. once per detection | Submit the complete detection collection in one batch per independently managed group |
| Posting a dispatcher callback or starting a task for every incoming result | Keep one latest pending result and consume it from one serialized, rate-limited display loop |
| Repainting fixed ROI, crosshairs, and labels with every camera frame | Keep static content in separate batches; update only changed content |
| Creating mutable brushes for every element or frame | Reuse frozen brushes, such as `Brushes.Lime` |
| Recreating HUD handles on every status update | Keep the handle and call `Update` only when content changes |

For example, the following operations belong to three different lifecycle stages;
do not put the initialization or cleanup in the per-frame callback:

```csharp
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

// Initialization: retain these handles for the display session.
var detections = viewer.Layers.CreateDrawingLayer("camera-detections");
var batch = detections.Add(Array.Empty<DrawingElement>());

// Display update: call from ONE serialized consumer at the chosen display rate.
// Build this collection only for the latest result selected for display.
batch.Replace(new DrawingElement[] {
    new CircleElement(new Point(100, 100), 8, Brushes.Lime, 2),
    new RectangleElement(new Rect(20, 20, 50, 40), Brushes.Yellow)
});

// A valid result with no detections clears content while retaining the visual.
batch.Replace(Array.Empty<DrawingElement>());

// Shutdown: first stop and await the producer/display loop, then release handles.
batch.Dispose();
viewer.Layers.RemoveLayer(detections);
```

Choose the display rate independently of acquisition/inference (for example,
30 or 60 updates per second, subject to measured rendering cost). Overwrite stale
pending results rather than queueing them. No new result means no `Replace` call;
a new empty result means `Replace(Array.Empty<DrawingElement>())`. Build drawing
elements after selecting the latest result so discarded results do not allocate
unused drawing objects. Release overwritten results if they own pooled buffers or
other resources, and do not mutate a result while the display consumer reads it.

`Replace` is synchronous and marshals visual work to the viewer thread. It does
not throttle, merge, or drop updates. The image pipeline's latest-frame queue does
not apply to overlay calls, and image submission plus overlay replacement is not
an atomic, frame-synchronized presentation. Consumers needing matching image and
detection results must track frame identity and define their own stale-result
policy. Do not hold a producer lock while calling viewer APIs.

`Replace` reuses the visual, **not all drawing allocations**: submission copies
the collection, shares immutable elements whose brushes are already frozen, and
clones elements/brushes when mutable brushes require a snapshot. Each update records
commands directly into the existing visual, avoiding intermediate `DrawingGroup`
and per-primitive geometry objects. WPF command buffers and resource references
still allocate. Equal pen styles share frozen
pens within one preparation using a bounded cache; text formatting still allocates.
Reusing the input array alone does not remove these allocations. Frozen brushes avoid brush cloning, but do
not make replacement allocation-free. Batching reduces visual count; it is not
a guarantee of zero GC or a particular frame rate.

`Clear`, layer removal, and viewer closure invalidate batch handles. If the host
allows Clear All Shapes during streaming, serialize that action with the display
loop and recreate the batch once before resuming; do not retry a disposed handle
on every frame. Stop subscriptions/timers and await in-flight updates before
teardown. Do not call `GC.Collect()` in the frame loop.

Measure allocation, GC counts and update latency with the expected marker count and zoom behavior; see [benchmark scope](validation.md#drawing-benchmark-scope).

## Reduce copies and conversion

- Keep the source's supported format and stride. Do not expand Gray8 to BGRA or reorder RGB/BGR solely for display. Native 8-bit CPU layouts are preserved where supported; Gray16, Gray32Float, Rgba32 and remapped Gray8 require display conversion.
- Reuse a producer buffer with `Copy`, or transfer a separately owned immutable buffer with `TakeOwnership`. `Copy` already rents internal memory; add producer pooling only after measuring allocations and retention. Never refill transferred memory early.
- Reuse one Viewer for the window lifetime. Stable image dimensions and display format allow its two CPU WriteableBitmaps to be reused. Change display range when needed, rather than requesting a redraw every frame.
- For pixels already on the GPU, use a supported GPU adapter to avoid a full-image round trip. GPU integration is not automatically faster for CPU input and still has allocation, synchronization and copy costs.

## Limit query work

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

## Keep callbacks short and measure the whole path

`OnCommitted` and `FrameCommitted` run on the Viewer STA. Do not perform encoding, device waits, heavy analysis, synchronous task waits or a submission-and-wait inside them. For asynchronous work, acquire an independent lease before the callback returns and release it when work finishes; the callback's borrowed lease cannot be retained as-is. Bound this background work too.

Measure producer conversion/copy cost, managed allocation, submission status counts, query duration and overlay update cost separately. Submission completion is not physical presentation latency. Run the [frame, drawing and query benchmarks](validation.md) for the path you change, then validate interaction and display on the target machine. Synthetic benchmarks do not establish sustained camera throughput or mixed-DPI behavior.
