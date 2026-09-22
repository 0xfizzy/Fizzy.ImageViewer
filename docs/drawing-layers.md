# Drawing and business layers

`Viewer.Layers` (also on `IViewerAPI`) owns image-coordinate business layers.
`Markers` starts at ZIndex 0 with hit testing disabled; `Measurements` starts at
1000 with hit testing enabled. Custom layers start at 100 with hit testing disabled.
Higher ZIndex draws on top; equal values use creation order. Image and HUD remain
below and above the business-layer container, respectively.

Each completed `LineStrength` measurement links its line, length label, and pixel
curve window. Closing the window removes its line and label; deleting the line
closes its window. Clearing measurements or disposing the viewer cleans up all
associated windows. Other measurements remain independent.

## Batch markers alongside measurements

```csharp
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Enums;
using System.Windows;
using System.Windows.Media;

var markers = viewer.Layers.CreateLayer("detections");
using var batch = markers.AddBatch(Enumerable.Range(0, 10000).Select(i =>
    new CircleElement(new Point(i % 100 * 10, i / 100 * 10), 3,
        Brushes.Red, 1, Brushes.Red) { ScaleMode = OverlayScaleMode.FixedSize }));

viewer.StartMeasure("Length"); // Also: Point, ROI, LineStrength (registered tool names).
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

## Ownership and compatibility

Collections and brush values are copied on submission; subsequent mutation is not
observed. Brushes are cloned and frozen on the calling thread (already frozen brushes
can be shared). Call from the owning thread for mutable WPF brushes. Non-freezable
resources, null elements, non-finite coordinates, nonpositive radii/line widths/font
sizes, and unsupported scale modes throw before content is changed.

`Replace` prepares all data before replacing the visual's content and preserves the
visual instance and stacking order. Failure keeps the previous drawing. All visual
operations dispatch to the viewer thread. `Dispose` removes a batch synchronously
and is idempotent; clear, layer removal and viewer closure invalidate handles.
`Replace` on an invalidated handle throws `ObjectDisposedException`.

Built-in layers cannot be removed. Names are nonempty and unique (case-sensitive).
Layer handles belong to one viewer and cannot be passed to another viewer's manager.
`Layers.Items` returns a snapshot. Clear preserves layer settings and subscriptions;
removal and closure release subscriptions and drawing resources.

**Behavior change:** `DrawLine`, `DrawCircle`, `DrawRectangle`, `DrawCrosshair`, and
`DrawText` now create single-element batches in `Markers`, without default selection
or editing. Use `Layers.<layer>.AddBatch(...)` for bulk drawing and layer selection.
For editable measurement shapes, continue using measurement tools and
`MeasureContext.AddShape`. Measurement shapes remain individual WPF UI elements.

`ClearShapes()` and the Clear All Shapes menu cancel measurement and clear every
business layer, including measurement results, while retaining the HUD. Clearing
`Measurements` also cancels an active measurement. `DrawHudText` is unchanged.
Snapshot export still exports image data, not overlay layers.

## Validation

```powershell
dotnet test tests/Fizzy.ImageViewer.Tests
dotnet run -c Release --project tools/DrawingBenchmark
```

The benchmark measures creation, replacement, and scale redraw including a layout
pass, against the previous per-Shape canvas path at 1,000 and 10,000 circles. Timings
are machine-dependent and exclude GPU presentation; visual counts and functional
behavior are the automated acceptance criteria.
