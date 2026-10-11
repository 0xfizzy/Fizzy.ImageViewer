# Custom measurement tools

[简体中文](../zh-CN/measurement-tools.md)

Implement a tool when an application needs its own interactive creation workflow. Start with [built-in measurements](measurements.md) if the existing tools are sufficient. Noninteractive annotations use [drawing layers](drawing-layers.md); there is no standalone measurement creation or saved-ROI restoration API.

## Create and register a tool

This complete tool creates a rectangle with the same editing, statistics and notifications as the built-in ROI. Each activation gets a new session.

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

Keep the registration alive while the feature is installed:

```csharp
using var registration = viewer.RegisterMeasurementTool(new CustomRoi());
viewer.ActivateMeasurementTool("custom-roi");
```

Choose a query using the [geometry/query table](measurements.md#queries-and-retained-results). Call `Complete()` for every item that should survive the session; returning `Finish` only ends input.

## Tool registration ownership

`RegisterMeasurementTool(tool)` returns an `IDisposable` handle for that registration.
Dispose it from any thread to revoke only that instance and cancel its active session.
Disposal is idempotent and safe after viewer closure; completed measurements survive.
The caller owns the tool object, which is never disposed by the registration.
Keep the handle for the installing component's lifetime. Ignoring it retains the
registration until global unregistration or viewer closure.
Registration captures `Id` and `DisplayName` once on the Viewer STA. After those
external getters return, the viewer checks its lifetime again before accepting the
registration. If a getter closes the viewer reentrantly, registration is rejected
with `ObjectDisposedException` and no tool is retained.

`UnregisterMeasurementTool(id)` is a viewer-wide management operation: it removes
whichever registration currently has that ID. Component cleanup should instead dispose
its own handle, so it cannot remove a replacement registered under the same ID.
Revocation takes effect before cancellation callbacks. Callback failures propagate to
explicit callers after cleanup; they do not restore the revoked registration.

Activation captures the registration instance before interrupting the previous session.
Invalid initial requests preserve the current interaction. Reentrant callbacks may
supersede a request; the superseded request never activates a same-ID replacement.
Built-in menu actions retain the registration captured when the menu opens and ignore
it if revoked, hidden, input-disabled or clearing when clicked.

## Extension boundary

Measurement tools use a stable, ordinal `Id` for programmatic lookup and a separate
`DisplayName` for menus. Registration rejects duplicate IDs; `UnregisterMeasurementTool`
removes only the registry entry and cancels an active session for that ID while leaving
completed measurement items intact. Menus are backed by the same registry and are
updated for subsequent openings when tools are registered or unregistered.

Use `viewer.ActivateMeasurementTool(MeasurementToolIds.Length)` for built-in tools; the other
constants are `Point`, `RectangleRoi` and `LineProfile`. Custom tools implement both `Id`
and `DisplayName`, and are installed with `RegisterMeasurementTool`. IDs are
case-sensitive; blank IDs or display names are rejected. `ActivateMeasurementTool` throws
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
commits one item and releases its reference to the creation context without finishing the
tool session. Each context, item and event snapshot
retains `Origin` with the registration's `ToolId` and activation's `SessionId`; multiple
items from one activation share the origin, while later activations get a new session ID.
Revoking a registration during cancellation invalidates an outer request to start that
registration, even when another registration subsequently uses the same ID.

## Reentry, cancellation and failure

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
even when a disposal callback starts it. Cleanup attempts every owned preview and propagates
combined session and preview failures to explicit callers. Framework input callbacks log
these failures; viewer shutdown includes them in `DisposeAsync`. Bulk measurement cleanup
rejects new measurement creation.
Once viewer shutdown begins, `ActivateMeasurementTool` throws `ObjectDisposedException`;
shutdown cleans both completed and unfinished measurements and waits for owned queries.

## Resources and threads

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
measurement. `AddResource` transfers ownership only when it succeeds. If it throws, the
call does not change ownership: a new resource remains the caller's responsibility, while
re-registering the same instance is rejected and leaves the measurement's existing ownership
intact. Do not share an owned resource across measurements. Unregistering a tool retains
completed measurements. Selection and deletion
target the measurement owner, which removes both its primary visual and label. Setting
`Measurements.IsVisible = false` cancels previews but retains completed items.
`Viewer.Hide()` retains the active tool session and previews; clear and viewer closure dispose all items.

Disposal revokes queries, executes cleanup callbacks, disposes resources, closes the plot
and removes visuals. Every stage is attempted after failures. Explicit disposal aggregates
cleanup errors. Explicit layer clearing and interaction cancellation propagate cleanup errors
after attempting all items; framework input callbacks log them. Viewer shutdown includes
resource cleanup failures in its shared `DisposeAsync` result. Disposal is idempotent and
does not retry resources whose disposal already ran.
Creation is rejected during bulk cleanup and shutdown. Geometry/result and viewer event
subscriber failures are logged and isolated. `Viewer.FrameCommitted` is a viewer-level notification, not a
query execution callback.

See [measurement results](measurements.md) and [Viewer lifetime](public-api.md).
