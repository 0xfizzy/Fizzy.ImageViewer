# Drawing and HUD

[简体中文](../zh-CN/drawing-layers.md)

Draw annotations in image coordinates and status text in screen coordinates. The snippets assume a live `viewer`; keep returned handles until the feature ends.

## Draw a marker

```csharp
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

using var marker = viewer.Layers.Markers.Add(
    new CircleElement(new Point(100, 100), 8, Brushes.Red));
marker.Replace(new CircleElement(new Point(120, 100), 8, Brushes.Lime));
```

`Viewer.Layers` (also on `IViewer`) owns image-coordinate business layers.
`CreateDrawingLayer` creates a custom drawing layer. `ClearContents` clears content
while retaining the layers; `RemoveLayer` removes a custom layer and its content.
`Markers` starts at ZIndex 0 with hit testing disabled; `Measurements` starts at
1000 with hit testing enabled. Custom layers start at 100 with hit testing disabled.
Higher ZIndex draws on top; equal values use creation order. Image and HUD remain
below and above the business-layer container, respectively.

`Markers` and custom layers are `DrawingLayer` instances with `Add` and `DrawingClicked`.
`Measurements` is a `MeasurementLayer`: tools create its model-owned content, and it has no
batch drawing API. `ViewerLayers` and the shared `ViewerLayer` base belong to `Fizzy.ImageViewer.Layers`.
Both layer types share `ViewerLayer` settings (`Name`, visibility, hit testing,
ZIndex and `Clear`); `Layers.Items` returns a snapshot of those shared handles.

## Drawing handles

`DrawingHandle` owns one independently managed drawing containing zero or more
`DrawingElement` values. Single elements and collections have the same creation,
replacement, hit-testing and disposal semantics:

```csharp
using var line = viewer.Layers.Markers.Add(new LineElement(start, end, Brushes.Red));
using var markers = viewer.Layers.Markers.Add(elements);
line.Replace(new LineElement(newStart, newEnd, Brushes.Red));
markers.Replace(updatedElements);
```

`Add(DrawingElement)` and `Add(IEnumerable<DrawingElement>)` both create exactly one
visual. `Replace` has the same two overloads and replaces the entire content; it
never appends. Either handle can move between single-element, collection and empty
content, including different shape types, without changing its identity or stacking
order. Empty collections keep the drawing alive but draw and hit nothing.
`DrawingClickedEventArgs.Drawing` identifies that same handle, not an individual
element. Dispose removes the whole drawing.

The `Viewer.DrawLine`, `DrawCircle`, `DrawRectangle`, `DrawCrosshair` and `DrawText`
helpers return the same `DrawingHandle` in the default Markers layer. Their names
specify the initial content, not a permanent shape constraint. Use the layer's `Add`
overloads to choose a layer or submit a collection. Each helper call creates its own
visual; use one collection per independently managed group for large marker sets.

## Batch markers alongside measurements

```csharp
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

var markers = viewer.Layers.CreateDrawingLayer("detections");
using var batch = markers.Add(Enumerable.Range(0, 10000).Select(i =>
    new CircleElement(new Point(i % 100 * 10, i / 100 * 10), 3,
        Brushes.Red, 1, Brushes.Red) { ScaleMode = OverlayScaleMode.FixedSize }));

viewer.ActivateMeasurementTool(MeasurementToolIds.Length); // Also: Point, RectangleRoi, LineProfile IDs.
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
markers.DrawingClicked += (_, e) => {
    // e.Drawing identifies the entire drawing; no per-element hit index or editing.
    // e.ImagePosition is in image pixels; e.Button identifies the mouse button.
};
// markers.Clear();                 // Invalidates every batch in this layer.
// viewer.Layers.RemoveLayer(markers); // Clears and removes a custom layer.
```

Each `Add` creates exactly one `DrawingVisual`, regardless of element count.
One call per marker still creates one visual per marker: submit the whole collection
to obtain batching. Text is drawn with `FormattedText`, not layout controls.
An empty batch retains its visual but draws and hits nothing.

## Coordinates, size and input

Drawing and measurement positions are continuous image coordinates: `(0, 0)` is
the top-left image boundary, and `(0.5, 0.5)` is the first pixel center. Positions
retain fractional values; drawing does not round them or add a center offset.
`PixelCoordinate` is an integer pixel index; use `ToImageCenter()` when marking
that pixel's center. Algorithms using integer coordinates for pixel centers must
be converted once in the consumer's display adapter by adding `(0.5, 0.5)` to
their continuous results. Do not round subpixel results or shift image boundaries,
mouse positions, or positions already expressed in viewer coordinates.

`OverlayScaleMode` describes scaling, independently of element positioning.
Each element supports the modes listed below; unsupported modes are rejected by
`Add` or `Replace` before content changes. Fixed screen dimensions use WPF DIPs,
not physical display pixels.

- Line, rectangle and circle default to `FixedStroke`: geometry uses image pixels,
  thickness uses screen DIPs. `ScaleWithImage` scales both geometry and thickness.
- Circle additionally supports `FixedSize`: radius and thickness use screen DIPs,
  while the center remains in image coordinates.
- Crosshair defaults to `FixedSize`, supports `FixedStroke` and `ScaleWithImage`; `ArmLength` is
  the distance from the center to each endpoint, with a center ring of radius `ArmLength / 2`.
- Text defaults to `FixedSize`: font size and offset use screen DIPs. `ScaleWithImage`
  scales them with the image. Its default typeface is Segoe UI.
- Pan updates only the shared transform. Scale-dependent batches are redrawn once
  per rendering cycle; DPI changes also refresh text drawing.
- Wheel zoom and middle-button pan use the common viewport input surface, including
  over hit-testable drawings and measurements. Pixel inspection uses that same surface.
- Hit testing uses actual drawing geometry (including fill and text glyphs), not
  batch bounds. An unfilled circle's interior and blank space pass input through.
  Overlapping batches resolve to the last-added batch in the topmost enabled layer.
- `DrawingClicked` runs on the viewer STA thread and consumes that mouse-down event.
  Handlers should remain short and marshal work to the application's UI when needed.
  Each subscriber failure is logged and isolated; later subscribers still receive the
  same click, and the event remains consumed.
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
`Layers.Items` returns a membership snapshot containing mutable layer handles. Clear preserves layer settings and subscriptions;
removal and closure release subscriptions and drawing resources.
Adding a `DrawingClicked` subscription dispatches to the viewer STA and rejects a removed
layer or stopping viewer. Unsubscription remains safe after removal or closure.

Global layer composition is available through `IViewer.Layers`. Borrowing
`IViewerDrawing` exposes only the default `Markers` layer and its `Draw*` helpers;
`IViewerMeasurements.Measurements` exposes the measurement layer. A `DrawingLayer`
grants management of that entire layer, including clearing other drawings and receiving
their click handles. Pass a `DrawingHandle` when a component only updates one existing
drawing. Keep layer access when it must create drawings or recreate them after global clear.

`DrawLine`, `DrawCircle`, `DrawRectangle`, `DrawCrosshair`, and
`DrawText` create drawings initially containing one element in `Markers`, without default selection
or editing. Use `Layers.<layer>.Add(...)` for bulk drawing and layer selection.
For editable measurement shapes, continue using measurement tools and
`IMeasurementToolContext.CreateMeasurement(geometry, options)`. Keep the returned
`IMeasurement`, update its geometry during creation and call `Complete()` to retain it.
The measurement owns its framework-generated visual, label, query and registered
resources until deletion, clear or viewer closure. Measurement shapes remain individual
WPF UI elements. `MeasurementOverlay` is internal; use layers, drawing handles or measurement
models for public access. WPF shape factories and their positioning metadata are internal;
standalone WPF elements cannot be attached through the public drawing API.
See [measurement contracts](measurements.md).

`viewer.Layers.ClearContents()` and the Clear All Shapes menu cancel measurement and clear every
business layer, including measurement results, while retaining the HUD. Clearing
uses a snapshot of layers taken before cancellation: layers created by callbacks
survive this clear, and layers removed by callbacks are skipped (removal already
clears them). Existing layers that remain attached are cleared in snapshot order.
Clearing
`Measurements` also cancels active measurement and editing sessions.
Explicit clear/remove operations preserve cancellation and content-cleanup failures while
attempting all remaining safe cleanup. Menu and input event boundaries log those failures;
viewer shutdown reports them through `DisposeAsync`.
Snapshot export still exports image data, not overlay layers.

`DrawText`, `TextElement` and `DrawHudText` all accept fractional `double` font sizes.
The image-coordinate `Draw*` helpers return replaceable `DrawingHandle` instances.

## HUD text

`viewer.HudLabelText` sets the upper-right label. `viewer.IsPixelInfoEnabled` enables or
disables pixel inspection independently of application HUD text and can be set before `Show()`.
Setting `HudLabelText` to null or an empty string clears and hides the label;
reading it then returns an empty string.

`HudTextHandle` and `AnchorAlignment` belong to `Fizzy.ImageViewer.Hud`.

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
