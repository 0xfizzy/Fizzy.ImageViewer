# Measurements and interaction

Built-in tools (`Point`, `Length`, `ROI`, `LineStrength`) create individual WPF
measurement elements. Batch markers continue to use `DrawingElement` and
`DrawingVisual`; the two rendering paths have independent purposes.

## Ownership and geometry

`Viewer.MeasurementStyle` configures newly created measurements independently for each
viewer. Assignment copies and freezes all brushes on the caller's thread before UI
dispatch. Existing shapes retain their normal and selected colors. Custom tools can
pass `IMeasureToolContext.Style` to the optional `style` parameter of `Shapes.Create*`;
omitting it uses immutable defaults. Shape helpers also snapshot supplied brushes.

Each internal `MeasurementItem` owns its immutable `MeasurementGeometry`, primary
visual, label, result, query subscription and optional plot window. A context-owned
registry maps visuals to their owner without putting business state in `Tag`.
Previews are owned items too; completing a preview enables its queries, and
cancelling disposes it. Internal shape metadata contains only presentation state.
Each visual is added once; completing a preview does not remove and re-add it.

Geometry uses source-image coordinates. A rectangle stores normalized opposite
corners. Editing updates the model first; the display adapter projects it to WPF.
Every actual geometry change increments a monotonic version and clears results.
During a rectangle drag, the opposite corner comes from the drag-start snapshot,
so crossing it does not change which corner is fixed. Labels follow geometry
immediately, even when queries are slow or fail.

ROI statistics and menu export use the same `PixelRegion.Clip` conversion of the
selected item's geometry. The menu freezes the frame lease and region together;
subsequent editing does not change an already captured export request. Editing
WPF properties directly is not a supported way to change built-in measurements.

Deletion, clear, plot-window closure and viewer closure converge on idempotent
item disposal: unsubscribe, detach/close the window, remove the label and primary
visual, then discard results. Removing a visual notifies observers **after** it is
removed, making repeated and reentrant removal harmless. Closing the viewer first
stops interaction, cancels queries and disposes items, then clears layers and
releases its frames. `DisposeAsync` waits for in-flight queries to release their own
leases, including sources that do not immediately honor cancellation.

## Interaction

The internal coordinator owns the selected item/shape and Idle, Editing and
Measuring modes. The edit manager owns its session and control-point visuals;
the measure manager owns the tool registry and current creation session.
The overlay only performs display, hit testing and selection styling.
It holds no coordinator or measurement-owner reference. The coordinator subscribes to
input and generic drawing-layer lifecycle notifications; the window composes the
measurement overlay into its drawing layer. A clear cancels the active session once,
cleans measurement owners, and invalidates batches even if cancellation fails.
Bulk clearing rejects new measurement/editing sessions and scope creation from cleanup
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

Cancellation captures the unfinished scopes before invoking the tool and cleans
only that snapshot; scopes created by the new session survive. Normal completion
captures unfinished scopes after `OnClick` returns, provided no newer session has
replaced it. Scope disposal during session cleanup may itself start a new session.
Cleanup attempts every captured scope and logs disposal failures. Bulk scope cleanup
during clear rejects new scope creation.
Once viewer shutdown begins, `StartMeasure` throws `ObjectDisposedException`;
shutdown cleans both completed and unfinished scopes and waits for owned queries.

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

A result retains the source frame ID. A newer frame with the same descriptor does
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
`DisplayName` for menus. Registration rejects duplicate IDs; `UnregisterMeasureMethod`
removes only the registry entry and cancels an active session for that ID while leaving
completed measurement items intact. Menus are backed by the same registry and are
updated for subsequent openings when tools are registered or unregistered.

Use `viewer.StartMeasure(MeasureToolIds.Length)` for built-in tools; the other
constants are `Point`, `ROI` and `LineStrength`. Custom tools implement both `Id`
and `DisplayName`, and are installed with `RegisterMeasureMethod`. IDs are
case-sensitive; blank IDs or display names are rejected. `StartMeasure` throws
`KeyNotFoundException` for an unknown ID and `InvalidOperationException` when the
measurement layer is hidden. `UnregisterMeasureMethod(id)` returns whether an entry
was removed. Call `CancelMeasure()` to end the active interaction session.

Built-in tool classes are internal and receive their internal context at construction.
The manager executes all tools through one internal protocol and registry; a small
adapter supplies the public context to custom tool callbacks. Built-ins are started
only by their tool IDs, rather than by constructing or inheriting tool classes.
Custom scopes do not participate in built-in completion/removal events, model editing
or the internal query scheduler.

The public extension boundary consists of `IMeasureMethod` and the `IMeasureToolContext` capability facade.
Custom tools should use `IMeasureToolContext.CreateScope()` to obtain an
`IMeasurementScope`. Register visuals with `AddShape`, disposable resources with
`AddResource`, and cleanup callbacks with `OnDispose`. Call `Complete()` before
returning `true` from `OnClick` to retain a result; incomplete scopes are released
when creation finishes or is cancelled (including tool changes and layer hiding).
Completed scopes survive cancellation and tool unregistration. Removing any owned
visual disposes its entire scope. Clear and viewer closure dispose all scopes.
`IMeasurementScope.Dispose()` explicitly cancels/removes a scope and is idempotent.

Scopes own resources, not their calculation logic. The scheduler, measurement
models, editor registry and typed query protocol remain internal. `Tag` contains
only display metadata. All scope operations run on the viewer STA. Cleanup callbacks
run before disposable resources, followed by visual removal. Cleanup continues after
failures; explicit disposal reports an aggregate exception, while framework cleanup
logs failures and continues. Visual observer failures are logged during scope cleanup.
Creating scopes during bulk cleanup is rejected. Register each visual with one owner
through `IMeasurementScope.AddShape`. Scope registration is the public tool path for
owning visuals, subscriptions and windows; internal visual attachment is not an
extension contract.
`FrameCommitted` remains a notification, not a query execution callback.

For example, this tool owns a marker and a frame notification subscription:

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Interfaces;
using System.Windows;

public sealed class CustomPoint : IMeasureMethod
{
    public string Id => "custom-point";
    public string DisplayName => "Custom point";
    public bool OnClick(Point point, IMeasureToolContext context)
    {
        var scope = context.CreateScope();
        try
        {
            var marker = Shapes.CreatePoint(point);
            scope.AddShape(marker);
            Action<Fizzy.ImageViewer.Frames.FrameInfo> changed = _ =>
                marker.ToolTip = "A new frame is available";
            context.FrameCommitted += changed;
            scope.OnDispose(() => context.FrameCommitted -= changed);
            scope.AddResource(new System.IO.MemoryStream()); // Owned external resource.
            scope.Complete();
            return true;
        }
        catch { scope.Dispose(); throw; }
    }
    public void OnMouseMove(Point point, IMeasureToolContext context) { }
    public void Cancel(IMeasureToolContext context) { } // Framework releases incomplete scopes.
}
```

`OverlayLayer` and `ViewerWindow.Layer1` are internal. Public access uses
`Viewer.Layers`, drawing handles, and measurement scopes.
The overlay does not offer standalone selection, deletion or editing; the
coordinator owns all interaction. Internal editor factories are registered by shape
key and create disposable editing sessions. Rectangle sessions retain the original
opposite corner throughout a drag; built-in measurement sessions update model
geometry and invalidate query results before projecting visuals.

The scheduler revokes subscriptions but does not dispose subscribers. Built-in
measurement items own their subscription handles; the pixel HUD is a separate
subscriber owned by the viewer.


