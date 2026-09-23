# Drawing and business layers

`Viewer.Layers` (also on `IViewerAPI`) owns image-coordinate business layers.
`Markers` starts at ZIndex 0 with hit testing disabled; `Measurements` starts at
1000 with hit testing enabled. Custom layers start at 100 with hit testing disabled.
Higher ZIndex draws on top; equal values use creation order. Image and HUD remain
below and above the business-layer container, respectively.

`Markers` and custom layers are `DrawingLayer` instances with `AddBatch` and `BatchClicked`.
`Measurements` is a `MeasurementLayer`: tools create its model-owned content, and it has no
batch drawing API. `ViewerLayers` and the shared `ViewerLayer` base belong to `Fizzy.ImageViewer.Layers`.
Both layer types share `ViewerLayer` settings (`Name`, visibility, hit testing,
ZIndex and `Clear`); `Layers.Items` returns a snapshot of those shared handles.

Each completed `LineStrength` measurement links its line, length label, and pixel
curve window. Closing the window removes its line and label; deleting the line
closes its window. Clearing measurements or disposing the viewer cleans up all
associated windows. Other measurements remain independent.

## Batch markers alongside measurements

```csharp
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

var markers = viewer.Layers.CreateLayer("detections");
using var batch = markers.AddBatch(Enumerable.Range(0, 10000).Select(i =>
    new CircleElement(new Point(i % 100 * 10, i / 100 * 10), 3,
        Brushes.Red, 1, Brushes.Red) { ScaleMode = OverlayScaleMode.FixedSize }));

viewer.StartMeasurement(MeasurementToolIds.Length); // Also: Point, ROI, LineStrength IDs.
// Measurement results and their editing controls belong to Measurements.
// Marker visuals do not intercept input by default.

batch.Replace(new DrawingElement[] {
    new CircleElement(new Point(100, 100), 8, Brushes.Lime, 2),
    new LineElement(new Point(10, 10), new Point(90, 90), Brushes.Cyan, 3),
    new RectangleElement(new Rect(20, 20, 50, 40), Brushes.Yellow),
    new CrosshairElement(new Point(200, 100), Brushes.Red, 12),
    new TextElement(new Point(100, 100), "target", Brushes.White, 18, new Vector(8, -20))
});
markers.IsVisible = true;
markers.ZIndex = 200;
markers.IsHitTestVisible = true;
markers.BatchClicked += (_, e) => {
    // e.Batch identifies the entire batch; no per-element hit index or editing.
    // e.ImagePosition is in image pixels; e.Button identifies the mouse button.
};
// markers.Clear();                 // Invalidates every batch in this layer.
// viewer.Layers.RemoveLayer(markers); // Clears and removes a custom layer.
```

Each `AddBatch` creates exactly one `DrawingVisual`, regardless of element count.
One call per marker still creates one visual per marker: submit the whole collection
to obtain batching. Text is drawn with `FormattedText`, not layout controls.
An empty batch retains its visual but draws and hits nothing.

## Live camera overlays: consumer requirements

Keep layers and batches alive for the display session. A camera frame is a data
update, not a reason to recreate the visual tree. Consumers of continuous camera
or detection streams must follow these rules:

| Avoid in the frame loop | Use instead |
| --- | --- |
| `Clear()` followed by `AddBatch(...)`, or disposing and recreating batches | Create each batch once and call `Replace(...)` on it |
| Calling `DrawCircle`, `DrawLine`, etc. once per detection | Submit the complete detection collection in one batch per independently managed group |
| Posting a dispatcher callback or starting a task for every incoming result | Keep one latest pending result and consume it from one serialized, rate-limited display loop |
| Repainting fixed ROI, crosshairs, and labels with every camera frame | Keep static content in separate batches; update only changed content |
| Creating mutable brushes for every element or frame | Reuse frozen brushes, such as `Brushes.Lime` |
| Recreating HUD handles on every status update | Keep the handle and call `Update` only when content changes |

For example, the following operations belong to three different lifecycle stages;
do not put the initialization or cleanup in the per-frame callback:

```csharp
// Initialization: retain these handles for the display session.
var detections = viewer.Layers.CreateLayer("camera-detections");
var batch = detections.AddBatch(Array.Empty<DrawingElement>());

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

Validate sustained workloads using allocated bytes per update, GC collection
counts, and update latency percentiles, including the expected marker/text count
and zoom behavior. The drawing benchmark below also measures average allocated
bytes and time across 50 warmed-up replacements on the viewer thread, alternating
between two prebuilt circle collections with different positions. It excludes
consumer result creation, cross-thread dispatch, and
physical presentation; it does not establish sustained camera-stream GC performance.

## Coordinates, size and input

- Line, rectangle and circle default to `FixedStroke`: geometry uses image pixels,
  thickness uses screen DIPs. `None` scales both geometry and thickness.
- Circle additionally supports `FixedSize`: radius and thickness use screen DIPs,
  while the center remains in image coordinates.
- Crosshair defaults to `FixedSize`, supports `FixedStroke` and `None`; `Size` is
  the half-length of an arm, with a center ring of radius `Size / 2`.
- Text defaults to `AnchoredLabel`: font size and offset use screen DIPs. `None`
  scales them with the image. Its default typeface is Segoe UI.
- Pan updates only the shared transform. Scale-dependent batches are redrawn once
  per rendering cycle; DPI changes also refresh text drawing.
- Hit testing uses actual drawing geometry (including fill and text glyphs), not
  batch bounds. An unfilled circle's interior and blank space pass input through.
  Overlapping batches resolve to the last-added batch in the topmost enabled layer.
- `BatchClicked` runs on the viewer STA thread and consumes that mouse-down event.
  Handlers should remain short and marshal work to the application's UI when needed.
- Measurement tools temporarily suppress all business-layer hit testing. Ending or
  cancelling measurement restores each layer's configured flag, including changes
  made during measurement. `IsHitTestVisible` reports the configured flag.

## Ownership and lifetime

Collections are copied on submission; subsequent mutation is not observed.
Immutable elements with frozen brushes can be shared. Mutable brushes are cloned
and frozen on the calling thread (already frozen brushes
can be shared). Call from the owning thread for mutable WPF brushes. Non-freezable
resources, null elements, non-finite coordinates, nonpositive radii/line widths/font
sizes, and unsupported scale modes throw before content is changed.

`Replace` validates snapshots and records drawing commands before publishing the
visual's content, preserving the visual instance and stacking order. A recording
failure discards the unfinished commands and keeps the previous drawing. All visual
operations dispatch to the viewer thread. `Dispose` removes a batch synchronously
and is idempotent; clear, layer removal and viewer closure invalidate handles.
`Replace` on an invalidated handle throws `ObjectDisposedException`.

Built-in layers cannot be removed. Names are nonempty and unique (case-sensitive).
Layer handles belong to one viewer and cannot be passed to another viewer's manager.
`Layers.Items` returns a snapshot. Clear preserves layer settings and subscriptions;
removal and closure release subscriptions and drawing resources.

`DrawLine`, `DrawCircle`, `DrawRectangle`, `DrawCrosshair`, and
`DrawText` create single-element batches in `Markers`, without default selection
or editing. Use `Layers.<layer>.AddBatch(...)` for bulk drawing and layer selection.
For editable measurement shapes, continue using measurement tools and
`IMeasurementToolContext.CreateMeasurement(geometry, options)`. Keep the returned
`IMeasurement`, update its geometry during creation and call `Complete()` to retain it.
The measurement owns its framework-generated visual, label, query and registered
resources until deletion, clear or viewer closure. Measurement shapes remain individual
WPF UI elements. `OverlayLayer` is internal; use layers, drawing handles or measurement
models for public access. WPF shape factories and their positioning metadata are internal;
standalone WPF elements cannot be attached through the public drawing API.
See [measurement contracts](measurements.md).

`ClearShapes()` and the Clear All Shapes menu cancel measurement and clear every
business layer, including measurement results, while retaining the HUD. Clearing
uses a snapshot of layers taken before cancellation: layers created by callbacks
survive this clear, and layers removed by callbacks are skipped (removal already
clears them). Existing layers that remain attached are cleared in snapshot order.
Clearing
`Measurements` also cancels active measurement and editing sessions.
Snapshot export still exports image data, not overlay layers.

## HUD text

`DrawHudText(text, brush, anchor, alignment, fontSize)` returns a typed
`HudTextHandle`. Keep this handle to update text and color; position, alignment and
font size remain fixed for its lifetime. Create a new handle to change layout.

```csharp
using var status = viewer.DrawHudText("Ready", Brushes.White);
status.Update("Running", Brushes.Lime);
```

Creation and updates snapshot brushes on the calling thread and dispatch visual
changes to the viewer STA. Mutable brushes must be accessed from their owning
thread. Null text/brushes, non-freezable brushes, invalid anchors or alignment,
and nonpositive or non-finite font sizes are rejected. Dispose removes the text
and is idempotent. Viewer closure invalidates all HUD handles; updating a disposed
or invalidated handle throws `ObjectDisposedException`. Clearing business layers
does not remove HUD text.

## Validation

```powershell
dotnet test tests/Fizzy.ImageViewer.Tests
dotnet run -c Release --project tools/DrawingBenchmark
```

The benchmark measures creation, replacement, and scale redraw including a layout
pass, against the previous per-Shape canvas path at 1,000 and 10,000 circles. Timings
are machine-dependent and exclude GPU presentation; visual counts and functional
behavior are the automated acceptance criteria.
