# Measurements and interaction

There is no standalone measurement creation or saved-ROI restoration API. Noninteractive drawings use drawing layers; editable measurements belong to tool sessions.

Built-in and custom tools create the same model-owned measurements through
`IMeasurementToolContext.CreateMeasurement`. Built-in IDs are `Point`, `Length`,
`RectangleRoi` and `LineProfile`. Batch markers continue to use `DrawingElement` and
`DrawingVisual`; the two rendering paths have independent purposes.

## Ownership and geometry

`MeasurementStyle` belongs to `.Measurements`. `Viewer.MeasurementStyle` configures newly created measurements independently for each
viewer. Assignment copies and freezes all brushes on the caller's thread before UI
dispatch. Existing measurements retain their normal and selected colors. `MeasurementOptions.Style`
overrides the viewer default for a new measurement and is snapshotted on creation.

Each measurement owns immutable `MeasurementGeometry`, a framework-generated primary
visual and label, an optional pixel query subscription and its registered resources.
MeasurementCollection registers measurement owners independently of visuals; a separate visual index
resolves hit testing. Each tool session owns its own unfinished measurements. A preview is
already an owned measurement; `Complete` retains it and enables queries. Cancelling
creation disposes unfinished measurements.
Visuals attach once and are not replaced on completion.

Geometry uses source-image coordinates. A rectangle stores normalized opposite
corners. Editing updates the model first; MeasurementPresentation projects it to WPF and owns
label formatting and the optional plot window.
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
removed, making repeated and reentrant removal harmless. Closing the viewer first
stops interaction, cancels queries and disposes items, then clears layers and
releases its frames. `DisposeAsync` waits for in-flight queries to release their own
leases, including sources that do not immediately honor cancellation.

## Interaction

The internal coordinator owns the selected measurement, Idle/Editing/Measuring mode,
active measurement tool and session version. The tool registry only stores registrations.
MeasurementEditController receives a MeasurementItem directly and owns its MeasurementEditSession
and control-point visuals. Hit testing resolves visuals through the collection before selection;
unregistered visuals cannot become measurement interaction targets. The session retains
drag-start geometry and writes changes through the measurement model; capability checks do not allocate a session.
ViewerInputBinding translates WPF input and applies pointer effects
without storing interaction state.
The overlay only performs display, hit testing and selection styling.
It holds no coordinator or measurement-owner reference. The coordinator receives
translated input and layer lifecycle notifications. MeasurementCollection subscribes to MeasurementLayer content cleanup without exposing the collection to the layer. Clearing first cancels input and selection, then always releases measurement
owners and their queries/resources, even if cancellation throws or no coordinator exists.
The layer remains in its clearing state throughout both phases. Batch layers independently
invalidate their batches.
Bulk clearing rejects new measurement/editing sessions and measurement creation from cleanup
callbacks; reentrant clears are idempotent, and other layers are still cleared after a failure.

Starting a valid measurement ends editing; starting valid editing cancels a
measurement. Invalid edit requests do not replace the current session. Escape
cancels creation or ends editing; edits already applied are retained. Delete ends
the selected editing session before disposing its owner. Clear, hiding the
measurement layer, disabling its configured hit testing and viewer shutdown
cancel active sessions and release mouse capture. Hiding retains completed items.
Losing capture finishes the current drag without deleting the item.
The internal mouse-capture boundary can be replaced in tests; capture failure
leaves the editing session active without leaving an active drag.

Temporary input suppression never changes configured layer hit-testing flags.
Restoring input recomputes effective flags for every layer, including layers
created while measuring. A hidden measurement layer does not start new tools.

During normal operation, a session started synchronously inside a tool callback
or completion notification takes precedence, even when it uses the same tool.
Returning or throwing from the old callback does not cancel that session or
restore its input state. This also applies when cancellation interrupts an outer
start request: the callback's session wins. Exceptions from tool callbacks propagate
to explicit API callers; framework input and plot-window callbacks log failures and keep
the viewer usable. Cleanup restores idle only if that operation still owns the session.
When operation and cleanup both fail, an aggregate retains the original failure first
and the cleanup failure after it; cleanup still attempts all owned resources.
Completion/removal notification subscribers are instead isolated: their exceptions
are logged and later subscribers still run.

Cancellation ends the outgoing creation context before invoking the tool and then releases
that session's unfinished items. Normal completion also ends the context and releases only
its unfinished items. Completed items belong to the measurement collection until removal.
A callback interrupted by a newer session cannot create through its old context; creation
throws `ObjectDisposedException`. Measurements created by the new session survive cleanup,
even when a disposal callback starts it. Cleanup attempts every owned preview and logs
disposal failures. Bulk measurement cleanup rejects new measurement creation.
Once viewer shutdown begins, `StartMeasurement` throws `ObjectDisposedException`;
shutdown cleans both completed and unfinished measurements and waits for owned queries.

## Query execution

`PixelQueryScheduler` runs one batch at a time. It captures typed pixel, line and
region requests on the viewer STA, performs all pixel-source operations off STA,
and publishes results on STA. Viewer owns this shared imaging service. Measurement items
subscribe through MeasurementRuntime, independently of the pixel HUD; disposing a measurement owner does not
stop other query clients. Pixel and line coordinates share one gather call;
region statistics execute individually so an unsupported region operation does
not discard successful pixel results. Query options control rates and result-age limits;
query metrics report execution, and each batch owns its frame lease until completion.
The oldest-due request selects the next group: either one region query or a merged gather
of due pixel and line requests. Each group publishes independently before another begins;
unselected clients retain their due time and capture fresh state on the next tick. This
provides fairness without giving gathers global priority over regions. Geometry invalidation
retains the next allowed execution deadline, so continuous dragging cannot bypass the
configured query rate.

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

`IQueryRuntime` supplies monotonic time, ticks, worker execution and UI
publication. Deterministic tests manually advance those boundaries without a real
window or wall-clock waits. The production runtime uses DispatcherTimer,
Stopwatch, Task.Run and Dispatcher publication.

## Extension boundary

Measurement tools use a stable, ordinal `Id` for programmatic lookup and a separate
`DisplayName` for menus. Registration rejects duplicate IDs; `UnregisterMeasurementTool`
removes only the registry entry and cancels an active session for that ID while leaving
completed measurement items intact. Menus are backed by the same registry and are
updated for subsequent openings when tools are registered or unregistered.

Use `viewer.StartMeasurement(MeasurementToolIds.Length)` for built-in tools; the other
constants are `Point`, `RectangleRoi` and `LineProfile`. Custom tools implement both `Id`
and `DisplayName`, and are installed with `RegisterMeasurementTool`. IDs are
case-sensitive; blank IDs or display names are rejected. `StartMeasurement` throws
`KeyNotFoundException` for an unknown ID and `InvalidOperationException` when the
measurement layer is hidden or its configured hit testing is disabled. `UnregisterMeasurementTool(id)` returns whether an entry
was removed. Call `EndInteraction()` to cancel creation or end editing; completed measurements and applied edits remain.

Built-in and custom registrations implement `IMeasurementTool`: `Id`, `DisplayName`, and
`CreateSession(IMeasurementToolContext)`. Each activation calls the factory on the viewer STA
and requires a new `IMeasurementToolSession`. Store preview handles, first-click coordinates
and other mutable interaction state in this session. The registration contains reusable
configuration; sharing it across viewers requires a thread-safe factory and configuration.

The session receives `OnClick(Point)`, `OnMouseMove(Point)` and `Cancel()` callbacks and retains
its own creation context. Returning `MeasurementClickResult.Finish` from `OnClick` ends input; `Continue` retains it; call `Complete()` on each
measurement to retain it. Unfinished measurements are released automatically. Normal completion
does not call `Cancel`; interruption calls it once after ending the creation context and releases
previews even if cancellation throws.
Every returned session is disposed exactly once on the STA, including normal completion,
callback failure and factory supersession. Put session-only resource cleanup in `Dispose`;
keep `Cancel` for cancellation-specific behavior. The creation context ends before either
callback. A disposal callback can start a replacement during normal operation; the old
session's cleanup cannot end that replacement.

Factories may create previews and may reenter viewer APIs. A newer activation takes precedence.
If a factory returns after its activation was interrupted, its returned session is cancelled
without changing the newer session. A factory failure or null return releases its unfinished
measurements; the factory owns cleanup of resources not yet transferred to a measurement.
A retained context cannot create measurements after its activation ends. For asynchronous
work, call `context.Finish()` to end only that context's activation after committing the
items to retain. It dispatches to the viewer STA and returns false for an ended,
superseded or closed context; it never ends a replacement session. `IMeasurement.Complete()`
commits one item and does not finish its tool session. Each context, item and event snapshot
retains `Origin` with the registration's `ToolId` and activation's `SessionId`; multiple
items from one activation share the origin, while later activations get a new session ID.
Revoking a registration during cancellation invalidates an outer request to start that
registration, even when another registration subsequently uses the same ID.

### Geometry, editing and notifications

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

### Observing built-in and custom measurements

Use `Viewer.MeasurementChanged` (also on `IViewer`) to observe completed items without
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
viewer.StartMeasurement(MeasurementToolIds.RectangleRoi);
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

### Queries and retained results

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

`IMeasurement.QueryResult` is null until successful publication. `QueryResultChanged` provides an
immutable `MeasurementQueryResult`, or null when a previous result becomes invalid. The result
includes measurement ID, geometry version, source `FrameInfo`, query kind and read-only
copies of samples, coordinates or channel statistics. It is safe to retain the result
after another frame, editing or removal. Collections never borrow scheduler buffers.

Results are a closed class family. Pattern-match `MeasurementSampleResult` to read
`Coordinates` and `Samples`: `Query` is `Pixel` for one pair or `LineProfile` for ordered
pairs. `MeasurementRegionResult` exposes a non-null clipped `Region` and `Channels`.
Common `MeasurementQueryResult` properties contain only provenance and `Query`, so unrelated
payload fields cannot be accidentally read. `None` produces no result.

Labels and plots update before notification. Geometry changes invalidate immediately;
query failure, descriptor change, missing targets and expiration clear obsolete results.
The scheduler and query protocol remain internal.

### Resources and threads

Tool callbacks, context creation and notifications run on the viewer STA. Returned
`IMeasurement` handles dispatch reads, updates and subscription changes to that STA,
so background work can update or complete a live preview without retaining a dispatcher.
Cancellation still disposes unfinished previews; late updates/completion then throw
`ObjectDisposedException`. The handle supplied by `MeasurementEventArgs.Measurement`
also supports application-driven updates to completed measurements. `Dispose` and event
unsubscription are safe after closure; `Id`, `Origin` and `IsDisposed` remain readable, while other
access requires a running viewer. Do not block STA callbacks waiting for workers that
call these handles.

Use `AddResource` and `OnDispose` to bind external resources and event subscriptions to the
measurement. Unregistering a tool retains completed measurements. Selection and deletion
target the measurement owner, which removes both its primary visual and label. Hiding cancels
previews but retains completed items; clear and viewer closure dispose all items.

Disposal revokes queries, executes cleanup callbacks, disposes resources, closes the plot
and removes visuals. Every stage is attempted after failures. Explicit disposal aggregates
cleanup errors; framework cleanup logs errors and continues. Disposal is idempotent.
Creation is rejected during bulk cleanup and shutdown. Geometry/result and viewer event
subscriber failures are logged and isolated. `Viewer.FrameCommitted` is a viewer-level notification, not a
query execution callback.

This custom ROI gets the same model editing, statistics, labels and viewer notifications
as the built-in ROI:

```csharp
using Fizzy.ImageViewer.Measurements;
using System.Windows;

public sealed class CustomRoi : IMeasurementTool
{
    public string Id => "custom-roi";
    public string DisplayName => "Custom ROI";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        => new Session(context);

    private sealed class Session(IMeasurementToolContext context) : IMeasurementToolSession
    {
        private IMeasurement? _preview;
        private Point _start;

        public MeasurementClickResult OnClick(Point point)
        {
            if (_preview is null or { IsDisposed: true })
            {
                _start = point;
                _preview = context.CreateMeasurement(
                    MeasurementGeometry.Rectangle(point, point),
                    new() { Query = MeasurementQueryKind.RegionStatistics });
                return MeasurementClickResult.Continue;
            }
            OnMouseMove(point);
            _preview.Complete();
            return MeasurementClickResult.Finish;
        }

        public void OnMouseMove(Point point)
        {
            if (_preview is { IsDisposed: false })
                _preview.UpdateGeometry(MeasurementGeometry.Rectangle(_start, point));
        }

        // The framework releases unfinished measurements after cancellation.
        public void Cancel() { }
        public void Dispose() { }
    }
}
```

The scheduler revokes subscriptions but does not dispose subscribers. Measurements own
their subscription handles; the pixel HUD is an independent subscriber owned by the viewer.

## Line-profile rendering

The X axis represents sample index and the Y axis pixel value. Both show numeric
ticks without axis titles. Horizontal/vertical dashed grids use lighter minor lines.
Curves use muted RGB colors and rounded joins without smoothing the samples. Tick spacing
adapts to the window size; axis drawings are cached until size, range or DPI changes.
The plot uses native WPF drawing with frozen shared pens and cached curve geometry.
Unchanged samples retain the geometry; data changes and resizing rebuild it.
Within each physical pixel column, rendering retains each finite run's endpoints
and minimum/maximum in sample order. Non-finite samples remain gaps. This bounds
dense curve detail by display resolution without changing raw measurement samples.
Plots consume immutable measurement-result samples directly. Channel buffers grow
geometrically and remain owned by the plot; data-only queries do not build RGB curve buffers. Query publication
delivers frame identity and read-only spans into the batch result together; callbacks consume
them synchronously.
WPF geometry serialization and frame queries still allocate when data changes.
