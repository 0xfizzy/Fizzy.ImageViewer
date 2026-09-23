# Measurements and interaction

Built-in and custom tools create the same model-owned measurements through
`IMeasurementToolContext.CreateMeasurement`. Built-in IDs are `Point`, `Length`,
`ROI` and `LineStrength`. Batch markers continue to use `DrawingElement` and
`DrawingVisual`; the two rendering paths have independent purposes.

## Ownership and geometry

`Viewer.MeasurementStyle` configures newly created measurements independently for each
viewer. Assignment copies and freezes all brushes on the caller's thread before UI
dispatch. Existing measurements retain their normal and selected colors. `MeasurementOptions.Style`
overrides the viewer default for a new measurement and is snapshotted on creation.

Each measurement owns immutable `MeasurementGeometry`, a framework-generated primary
visual and label, an optional pixel query subscription and its registered resources.
The context has one registry mapping visuals to their measurement owner. A preview is
already an owned measurement; `Complete` retains it and enables queries. Cancelling
creation disposes unfinished measurements. `Tag` remains caller-owned presentation data.
Visuals attach once and are not replaced on completion.

Geometry uses source-image coordinates. A rectangle stores normalized opposite
corners. Editing updates the model first; the display adapter projects it to WPF.
Every actual geometry change increments `IMeasurement.GeometryVersion` and clears results.
An equal geometry update is a no-op. Geometry kind cannot change after creation.
During a rectangle drag, the opposite corner comes from the drag-start snapshot,
so crossing it does not change which corner is fixed. Labels follow geometry
immediately, even when queries are slow or fail.

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

The internal coordinator owns the selected item/shape, Idle/Editing/Measuring mode,
active measurement tool and session version. The tool registry only stores registrations.
The edit manager directly owns a MeasurementEditSession and control-point visuals and borrows
a measurement lookup delegate. The session retains drag-start geometry and writes changes
through the measurement model; capability checks do not allocate a session.
ViewerInputBinding translates WPF input and applies pointer effects
without storing interaction state.
The overlay only performs display, hit testing and selection styling.
It holds no coordinator or measurement-owner reference. The coordinator receives
translated input and generic drawing-layer lifecycle notifications; the window composes the
measurement overlay into its drawing layer. A clear cancels the active session once,
cleans measurement owners, and invalidates batches even if cancellation fails.
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
to their caller; cleanup restores idle only if that operation still owns the session.
Completion/removal notification subscribers are instead isolated: their exceptions
are logged and later subscribers still run.

Cancellation captures the unfinished measurements before invoking the tool and cleans
only that snapshot; measurements created by the new session survive. Normal completion
captures unfinished measurements after `OnClick` returns, provided no newer session has
replaced it. Measurement disposal during session cleanup may itself start a new session.
Cleanup attempts every captured measurement and logs disposal failures. Bulk measurement cleanup
during clear rejects new measurement creation.
Once viewer shutdown begins, `StartMeasurement` throws `ObjectDisposedException`;
shutdown cleans both completed and unfinished measurements and waits for owned queries.

## Query execution

`PixelQueryScheduler` runs one batch at a time. It captures typed pixel, line and
region requests on the viewer STA, performs all pixel-source operations off STA,
and publishes results on STA. Viewer owns this shared imaging service. Measurement
context and pixel HUD subscribe independently; disposing a measurement owner does not
stop other query clients. Pixel and line coordinates share one gather call;
region statistics execute individually so an unsupported region operation does
not discard successful pixel results. Query options control rates and result-age limits;
query metrics report execution, and each batch owns its frame lease until completion.

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
constants are `Point`, `ROI` and `LineStrength`. Custom tools implement both `Id`
and `DisplayName`, and are installed with `RegisterMeasurementTool`. IDs are
case-sensitive; blank IDs or display names are rejected. `StartMeasurement` throws
`KeyNotFoundException` for an unknown ID and `InvalidOperationException` when the
measurement layer is hidden. `UnregisterMeasurementTool(id)` returns whether an entry
was removed. Call `CancelMeasurement()` to end the active interaction session.

Built-in tool classes are internal but implement the same `IMeasurementTool` protocol
as extensions. Both receive the public context in each callback. A tool returns `true`
from `OnClick` to end its input session; call `Complete()` on each result to retain it.
Returning `true` does not implicitly complete unfinished measurements.

### Geometry, editing and notifications

Create geometry with `MeasurementGeometry.Point`, `Crosshair`, `Line`, `Rectangle` or
`Circle`. Point and crosshair use `Start`; lines use `Start`/`End`; rectangles normalize
opposite corners. Circles use `Start` as center and `Radius`; `End` equals the center.
Coordinates and extents must be finite; circle radii must be nonnegative.

The framework creates the shape, label and supported control points. Use
`UpdateGeometry` for previews and programmatic updates. Directly changing WPF properties
is not a measurement mutation API. `GeometryChanged` reports the new immutable geometry
after model, label and result invalidation have been applied. Use it to write edits back
to an application model. A multi-shape tool creates multiple measurements, each with its
own identity, completion and removal lifetime; there is no arbitrary visual attachment path.

`Viewer.MeasurementCompleted` and `MeasurementRemoved` cover every tool. Completion fires
once, excludes previews and does not imply query readiness. Removal fires only for a
previously completed item. Snapshots include `Id`, complete `Geometry` and `GeometryVersion`.
The event's removal handle can be disposed from any thread, repeatedly or after closure.

### Queries and retained results

`MeasurementOptions.Query` selects a closed set of capabilities:

| Query | Supported geometry | Result |
| --- | --- | --- |
| `None` (default) | All | Geometry label; no pixel subscription |
| `Pixel` | Point, crosshair | Sample and integer source coordinate |
| `LineProfile` | Line | Ordered samples and source coordinates |
| `RegionStatistics` | Rectangle | Clipped region and channel statistics |

Incompatible combinations are rejected before any visual attaches. Pixel queries use
floor coordinates and reject points outside the frame. Line profiles clip to the frame;
ROI queries and export share `PixelRegion.Clip`. Empty targets have no result.
`ShowLineProfile` requires `LineProfile`; it opens an owned plot window on completion.
Closing that window removes the measurement. Built-in LineStrength enables it; custom
line queries default to data only. Built-in ROI explicitly selects region statistics.

`IMeasurement.Result` is null until successful publication. `ResultChanged` provides an
immutable `MeasurementResult`, or null when a previous result becomes invalid. The result
includes measurement ID, geometry version, source `FrameInfo`, query kind and read-only
copies of samples, coordinates or channel statistics. It is safe to retain the result
after another frame, editing or removal. Collections never borrow scheduler buffers.
Labels and plots update before notification. Geometry changes invalidate immediately;
query failure, descriptor change, missing targets and expiration clear obsolete results.
The scheduler and query protocol remain internal.

### Resources and threads

Tool callbacks, measurement operations and notifications use the viewer STA. Use
`AddResource` and `OnDispose` to bind external resources and event subscriptions to the
measurement. Unregistering a tool retains completed measurements. Removing its primary
visual or label removes the entire measurement. Hiding cancels previews but retains
completed items; clear and viewer closure dispose all items.

Disposal revokes queries, executes cleanup callbacks, disposes resources, closes the plot
and removes visuals. Every stage is attempted after failures. Explicit disposal aggregates
cleanup errors; framework cleanup logs errors and continues. Disposal is idempotent.
Creation is rejected during bulk cleanup and shutdown. Geometry/result and viewer event
subscriber failures are logged and isolated. `FrameCommitted` is a notification, not a
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
    private IMeasurement? _preview;
    private Point _start;

    public bool OnClick(Point point, IMeasurementToolContext context)
    {
        if (_preview is null or { IsDisposed: true })
        {
            _start = point;
            _preview = context.CreateMeasurement(
                MeasurementGeometry.Rectangle(point, point),
                new() { Query = MeasurementQuery.RegionStatistics });
            return false;
        }
        OnMouseMove(point, context);
        var completed = _preview;
        _preview = null; // Completion subscribers may synchronously restart this tool.
        completed.Complete();
        return true;
    }

    public void OnMouseMove(Point point, IMeasurementToolContext context)
    {
        if (_preview is { IsDisposed: false })
            _preview.UpdateGeometry(MeasurementGeometry.Rectangle(_start, point));
    }

    public void Cancel(IMeasurementToolContext context)
    {
        var preview = _preview;
        _preview = null;
        preview?.Dispose();
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
Sample buffers grow geometrically and remain owned by their plot. Query publication
uses read-only spans into the batch result; callbacks consume them synchronously.
WPF geometry serialization and frame queries still allocate when data changes.
