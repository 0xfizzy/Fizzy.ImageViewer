# Measurements and interaction

[简体中文](../zh-CN/measurements.md)

Use built-in tools to measure points, lengths, rectangular regions and line profiles. Positions and lengths use image pixels; noninteractive annotations belong in [drawing layers](drawing-layers.md).

## Activate a built-in tool

The snippets assume a live `viewer` from [Getting started](getting-started.md).

```csharp
using Fizzy.ImageViewer.Measurements;

viewer.ActivateMeasurementTool(MeasurementToolIds.Length);
```

| Tool ID | Interaction | Result |
| --- | --- | --- |
| `Point` | Click a point | Pixel value |
| `Length` | Click two endpoints | Length in image pixels |
| `RectangleRoi` | Click opposite corners | Region statistics |
| `LineProfile` | Click two endpoints | Pixel samples and a profile window |

Use the context menu to choose a tool, or activate its ID. Unknown IDs throw `KeyNotFoundException`; a hidden or input-disabled measurement layer rejects activation with `InvalidOperationException`. Activation after shutdown starts throws `ObjectDisposedException`.

## Observing built-in and custom measurements

Use `Viewer.MeasurementChanged` (also on `IViewerCapabilities`) to observe completed items without
implementing a tool or accessing WPF visuals. Geometry edits, query publication and result
invalidation all carry the measurement ID, immutable geometry/version and current immutable
`QueryResult`. `QueryResult` is null after geometry changes, query failure or expiration. Previews do
not emit this event, and an unchanged geometry or already-empty result does not emit it.

```csharp
viewer.MeasurementChanged += (_, e) =>
{
    var id = e.Snapshot.Id;
    var geometry = e.Snapshot.Geometry;
    var result = e.QueryResult; // Null means the measurement currently has no valid pixel result.
    // Retain these immutable values or dispatch them to the application's UI.
};
viewer.ActivateMeasurementTool(MeasurementToolIds.RectangleRoi);
```

Viewer and handle notifications share one FIFO queue on the viewer STA. Each mutation
captures immutable payloads and its subscribers before delivery; reentrant mutations append
their events after the current mutation's notifications. Every subscriber sees nondecreasing
geometry versions, completion precedes removal, and removal is terminal. The live handle
may already reflect a later mutation or disposal while an older snapshot is delivered.
Closing from a subscriber preserves queued measurement events and emits `Closed` after
removals. Subscriber exceptions are logged and isolated. The event's `Measurement` can be
disposed from any thread, including reentrantly from a callback. Completion and removal events carry the same result snapshot field,
but completion does not promise query readiness. Custom tools may additionally subscribe to
`IMeasurement.GeometryChanged` and `QueryResultChanged` on their own items.

## Edit and remove

Click a completed measurement to select it, then choose **Edit** in the context menu and drag its control points. Delete removes the selected item; Escape ends creation or editing and retains already-applied edits. Use the event's `Measurement` handle for programmatic `UpdateGeometry` or `Dispose`, and `viewer.Measurements.Clear()` to remove all measurements.

Handle events use `EventHandler<MeasurementGeometry>` and
`EventHandler<MeasurementQueryResult?>`, with the measurement itself as sender.
Adding subscribers rejects removed measurements and stopping viewers; removing them
remains safe. Captured notifications survive unsubscription. See the shared
[event subscription contract](public-api.md#event-subscriptions), including synchronous
exception isolation and subscriber responsibility for async-void work.

## Geometry, editing and notifications

Create geometry with `MeasurementGeometry.Point`, `Crosshair`, `Line`, `Rectangle` or
`Circle`. Point and crosshair use `Position`; lines use `Start`/`End`; rectangles normalize
opposite corners. Circles use `Center` and `Radius`. Factories return the sealed concrete
records `PointMeasurementGeometry`, `CrosshairMeasurementGeometry`, `LineMeasurementGeometry`,
`RectangleMeasurementGeometry` and `CircleMeasurementGeometry`. Pattern-match these types
when reading a handle or event snapshot; unsupported coordinate properties do not exist.
Kinds use the measurement-specific `MeasurementGeometryKind` enum. `Bounds` returns normalized
image-space bounds: endpoint bounds for lines and rectangles, diameter bounds for circles,
and zero extent for points and crosshairs. Line endpoints retain their original order.
Coordinates and extents must be finite; circle radii must be nonnegative.

The framework creates the shape, label and supported control points. Coincident control
points prioritize extent handles, allowing zero-radius circles, zero-length lines and
collapsed rectangles to expand. The entire label uses inverse scale, keeping text,
padding and offset fixed in screen space while its anchor follows image coordinates. Use
`UpdateGeometry` for previews and programmatic updates. Directly changing WPF properties
is not a measurement mutation API. `GeometryChanged` reports the new immutable geometry
after model, label and result invalidation have been applied. Use it to write edits back
to an application model. A multi-shape tool creates multiple measurements, each with its
own identity, completion and removal lifetime; there is no arbitrary visual attachment path.

`Viewer.MeasurementCompleted` and `MeasurementRemoved` cover every tool. Completion fires
once, excludes previews and does not imply query readiness. Removal fires only for a
previously completed item. Snapshots include `Id`, complete `Geometry` and `GeometryVersion`.
The event's `Measurement` handle supports updates and disposal from any thread; disposal
is idempotent and safe after closure.

## Queries and retained results

`MeasurementOptions.Query` selects a `MeasurementQueryKind`:

| Query | Supported geometry | Result |
| --- | --- | --- |
| `None` (default) | All | Geometry label; no pixel subscription |
| `Pixel` | Point, crosshair | Sample and integer source coordinate |
| `LineProfile` | Line | Ordered samples and source coordinates |
| `RegionStatistics` | Rectangle | Clipped region and channel statistics |

Incompatible combinations are rejected before any visual attaches. Pixel queries use
floor coordinates and reject points outside the frame. Line profiles clip to the frame;
ROI queries and export share `PixelRegion.Clip`. Empty targets have no result.
Use `MeasurementQueryKind.Pixel`, `LineProfile`, `RegionStatistics` or `None`.
Set `ShowProfileWindow = true` independently to request an owned plot window on completion.
Closing that window removes the measurement. This option requires a line-profile query.
The built-in line-profile tool enables it; explicit queries default to data only. Geometry compatibility
and the window/query combination are validated before attachment.

`LineMeasurementGeometry.Length` provides the geometric length in image pixels without reading a frame. A length-only measurement therefore has no query result.
Distance calculation avoids intermediate square overflow and underflow; positive infinity
is returned only when the resulting length cannot be represented by a `double`.

`IMeasurement.QueryResult` is null until successful publication. `QueryResultChanged` provides an
immutable `MeasurementQueryResult`, or null when a previous result becomes invalid. The result
includes measurement ID, geometry version, source `FrameInfo`, query kind and read-only
copies of samples, coordinates or channel statistics. It is safe to retain the result
after another frame, editing or removal. Collections never borrow scheduler buffers.

Results are a closed class family. Pattern-match `MeasurementSampleResult` to read
`Coordinates` and `Samples`: `Query` is `Pixel` for one pair or `LineProfile` for ordered
pairs. `MeasurementRegionStatisticsResult` exposes a non-null clipped `Region` and `Channels`.
Common `MeasurementQueryResult` properties contain only provenance and `Query`, so unrelated
payload fields cannot be accidentally read. `None` produces no result.

Labels and plots update before notification. Geometry changes invalidate immediately;
query failure, descriptor change, missing targets and expiration clear obsolete results.
The scheduler and query protocol remain internal.

## Ownership and geometry

`IViewerMeasurements.Measurements` exposes the entire measurement layer for visibility,
input policy and clearing. Clear cancels the current interaction and removes all measurements,
while retaining drawing layers and HUD. Tool registration and `EndInteraction` likewise
manage viewer-wide state, not only resources created by the caller. Within a tool session,
use the context's `Finish` to end that activation without affecting a replacement session.
Measurement events carry immutable snapshots alongside a live editable/removable handle;
subscribing is therefore not a read-only observation capability.

`MeasurementStyle` belongs to `.Measurements`. `Viewer.MeasurementStyle` configures newly created measurements independently for each
viewer. Assignment copies and freezes all brushes on the caller's thread before UI
dispatch. Existing measurements retain their normal and selected colors. `MeasurementOptions.Style`
overrides the viewer default for a new measurement and is snapshotted on creation.

Each measurement owns immutable `MeasurementGeometry`, a framework-generated primary
visual and label, an optional pixel query subscription and its registered resources.
Each tool session owns its own unfinished measurements. A preview is
already an owned measurement; `Complete` retains it and enables queries. Cancelling
creation disposes unfinished measurements.
Visuals attach once and are not replaced on completion.

Geometry uses source-image coordinates. A rectangle stores normalized opposite
corners. Editing updates the model before the visual, label and optional plot window.
Point and line queries use half-open pixel coverage `[0, Width) × [0, Height)`.
Line queries floor clipped endpoints to pixel indices, so fractional coordinates in
the last row/column remain sampleable. Input direction and endpoint pixels are retained;
lines lying on the exclusive right/bottom boundary are outside the image.
Every actual geometry change increments `IMeasurement.GeometryVersion` and clears results.
An equal geometry update is a no-op. Geometry kind cannot change after creation.
During a rectangle drag, the opposite corner comes from the drag-start snapshot,
so crossing it does not change which corner is fixed. Programmatic updates refresh
the primary visual, label and editing control points before public notifications.
An external update during dragging replaces the drag baseline; subsequent pointer
moves use the same control-point index in that new geometry, preserving its fixed anchor.
Labels follow geometry immediately, even when queries are slow or fail.

ROI statistics and menu export use the same `PixelRegion.Clip` conversion of the
selected item's geometry. The menu freezes the frame lease and region together;
subsequent editing does not change an already captured export request. Editing
WPF properties directly is not a supported way to change measurements.

Deletion, clear, plot-window closure and viewer closure converge on idempotent
item disposal: release specialized resources, then remove the label and primary
visual even if a resource fails to close. Removing a visual notifies observers **after** it is
removed, making repeated and reentrant removal harmless. Closing the viewer stops
admission and the frame pipeline, ends interaction, cancels queries, disposes items
and clears layers. In-flight rendering and queries retain their own frame leases;
`DisposeAsync` waits for their completion, including sources that do not immediately
honor cancellation. Independent caller-owned leases may outlive disposal.

## Interaction

Starting a valid measurement ends editing; starting valid editing cancels a
measurement. Invalid edit requests do not replace the current session. Escape
cancels creation or ends editing; edits already applied are retained. Delete ends
the selected editing session before disposing its owner. Clear, hiding the
measurement layer, disabling its configured hit testing and viewer shutdown
cancel active sessions and release mouse capture. Hiding retains completed items.
Losing capture finishes the current drag without deleting the item.
Capture failure leaves the editing session active without leaving an active drag.

Temporary input suppression never changes configured layer hit-testing flags.
Restoring input recomputes effective flags for every layer, including layers
created while measuring. A hidden measurement layer does not start new tools.

## Query execution

Configure shared query rates with `QueryOptions` and inspect `QueryMetrics`; see [query scheduling](frame-pipeline.md#pixel-access-and-measurements).

Subscriptions have distinct lifetimes. Publication requires the same registration,
stable measurement identity, geometry version and frame descriptor. Re-registering
an object never accepts work from its previous subscription. Geometry changes,
invalid regions, cancellation and disposal prevent stale publication. Query and
publication failures are isolated per subscriber; failed results remain invalid.

The pixel HUD alone opts into a moving-result policy: geometry changes retain the
last coordinate/value pair, and completed samples can publish within the same
interaction session. Session and descriptor changes still reject old work. Its
completion-based 10 Hz cap and 300 ms display retention do not alter other tools'
rates or geometry validation. See [pixel query contracts](frame-pipeline.md).

The scheduler tracks the source frame ID of published results. A newer frame with the same descriptor does
not by itself reject an in-flight result: it may publish within `MaxResultAge`.
This bounded-age policy prevents starvation when video arrives faster than queries
finish. Changed descriptors and expired results are rejected. A result from an
unchanged frozen frame remains valid without repeated queries.

## Threads and disposal

Measurement handles dispatch reads, updates and subscriptions to the Viewer STA. Keep callbacks short and never wait there for a worker that calls Viewer APIs. After closure, `Dispose` and unsubscription remain safe; `Id`, `Origin` and `IsDisposed` remain readable. Other access requires a running Viewer. Cleanup errors from explicit disposal/clear propagate after safe cleanup; shutdown cleanup failures enter `DisposeAsync`. Notification exceptions are logged and isolated.

## Custom tools

See [custom measurement tools](measurement-tools.md) for registration, session creation, resource transfer, reentry and failure handling.

The profile window plots sample index against pixel value without changing stored samples. Closing it removes its measurement. Rendering details belong to [architecture](architecture.md#line-profile-rendering).
