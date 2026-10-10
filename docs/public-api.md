# Supported public API

[简体中文](zh-CN/public-api.md)

`Viewer` constructs an independent STA window. `IViewer` is its complete consumer
contract, including `HudLabelText`, `QueryOptions` and `QueryMetrics`. `Viewer` is sealed;
application adapters own an instance and use its public API. Raw-window access is not supported.

The generated **API reference** lists public types and members. Source XML defines
symbol-level constraints; component guides describe cross-API protocols. Begin with
the [complete examples](getting-started.md).

## Borrowing viewer capabilities

`IViewer` aggregates the following interfaces and `IAsyncDisposable`. `Viewer` implements
all capabilities on the same instance; callers can accept the narrow interface they need.
Only the owning host disposes the viewer. Borrowed capabilities share its STA, state and
shutdown gate and have no independent disposal contract.

| Interface | Namespace | Responsibility |
| --- | --- | --- |
| `IFrameSink` | `.Frames` | Submit owned frames and await commit/drop results |
| `ICommittedFrameSource` | `.Frames` | Acquire current original-frame leases and observe commits |
| `IViewerWindow` | Root | Window visibility, placement, title, close policy and `Closed` |
| `IViewerDisplay` | `.Viewport` | Fit the image to the viewport and configure display range |
| `ISnapshotSource` | `.Snapshots` | Capture independently owned full-frame or region snapshots |
| `IViewerDrawing` | `.Drawing` | Default `Markers` layer and drawing helpers |
| `IViewerMeasurements` | `.Measurements` | `Measurements` layer, tools, interaction, style and events |
| `IViewerHud` | `.Hud` | Screen-space text and pixel inspection visibility |
| `IViewerMenu` | `.Menus` | Revocable menu registration |
| `IViewerQueries` | `.Imaging` | Shared query settings and metrics for measurements and HUD |

```csharp
using Fizzy.ImageViewer.Frames;

static ValueTask<FrameSubmitResult> PublishAsync(IFrameSink sink, ImageFrame frame,
    CancellationToken ct = default) => sink.SubmitFrameAsync(frame, ct: ct);
```

Submission transfers ownership even when the sink rejects or cancels the frame; alternate
implementations and test doubles must release it on every terminal path. Frame readers
own their acquired leases. A commit event and a subsequent acquisition are not atomic;
use `FrameSubmissionOptions.OnCommitted` for a specific submission's borrowed lease.

Global layer composition belongs only to `IViewer.Layers`: it can enumerate, create,
remove and clear layers across both drawing and measurements. `Layers.Items` snapshots
membership, but its elements remain live mutable handles. `IViewerDrawing.Markers` and
`IViewerMeasurements.Measurements` expose their respective entire layers; clearing one
does not clear the other. A renderer needing a particular layer can accept `DrawingLayer`;
a component updating only an existing drawing can accept `DrawingHandle`. Layer access
includes all content and click handles in that layer, not only the caller's creations.

Measurement events include immutable snapshots and a live editable/removable measurement
handle. Measurement tool registration and `EndInteraction` manage the viewer-wide state;
a tool session should use its context's `Finish` to end only itself. Query configuration
affects the shared scheduler for measurements and pixel HUD, including other callers.
Menu and HUD handles instead operate on their individual registration or text.

These interfaces describe normal typed dependencies, not security isolation. The same
`Viewer` implements all capabilities and is the sender of viewer events; casts can recover
the complete facade. They do not make drawing independent of WPF. `Closed` is a window
notification; the owner still awaits `IViewer.DisposeAsync()` for complete shutdown.

## Threads and window lifetime

The optional `ILogger<Viewer>` constructor argument defaults to no logging. Omitting it
or passing `null` uses `NullLogger<Viewer>.Instance`; pass a logger to integrate with
the application logging system. `new Viewer()` requires no logging setup.

Construction shows the window by default. Pass `showWindow: false` to create a hidden
viewer, configure it and subscribe to events before calling `Show()`. A hidden viewer
has a running STA and can accept frames; its owner must await `DisposeAsync()` even
if it is never shown. Application adapters should register their resources before
showing, and dispose the viewer if their own initialization fails.

Window methods and properties synchronously dispatch to the viewer STA and throw
`ObjectDisposedException` once disposal begins. `Show` restores a minimized window,
shows it and requests activation. `Hide` hides without clearing frames, drawings or
measurements. `Minimize` changes window state without hiding it. `IsVisible` follows
WPF visibility (a shown minimized window is still visible); `IsMinimized` is independent.
Minimizing an already hidden window does not implicitly show it.

`DisposeAsync` closes the window and waits for owned background work and the STA to stop.
Repeated calls observe one completion task and its fixed success or failure result.
Resource cleanup failures are reported after all remaining safe cleanup and STA exit;
subscriber notification failures are logged and isolated. `Closed` subscriptions are
released when that terminal notification is delivered.
Construction validates window bounds before starting the STA. If initialization fails,
safe resource cleanup runs on the owning thread and background work is drained before
the original exception is rethrown. If cleanup also fails, an aggregate preserves both
initialization and cleanup errors. Resources whose safe detachment cannot be established
remain isolated rather than being released or reused.
Application adapters route user closure (`Closed`) and explicit adapter disposal to
one idempotent cleanup task, release application resources and await the owned viewer's
`DisposeAsync`. Keep measurement subscriptions until window cleanup finishes when
the adapter forwards removal notifications.
`Closed` is a window notification, not a substitute for awaiting disposal. There is no
separate public `Close` method. Event callbacks run on the viewer STA; do not block them
waiting for shutdown or for work that needs that dispatcher.

Frame submission retains its explicit ownership and terminal-result contract: submission
after closure consumes the frame and returns `Closed`, rather than using the window
property exception policy. Drawing and HUD handle disposal is idempotent after closure.
See [frame contracts](frame-pipeline.md) and [drawing contracts](drawing-layers.md).

`HudLabelText` controls the upper-right HUD text. `IsPixelInfoEnabled` defaults to true and
controls bottom-left pixel inspection and its query subscription; the context menu uses
the same state. Both properties can be configured before showing the window.
`EndInteraction()` cancels unfinished creation or ends editing, preserving completed
measurements and edits already applied.

`FitToViewport` centers the full image and may choose a scale outside the ordinary
wheel range of 0.01–100. At such a scale, the wheel permits steps toward that range
and rejects steps farther away, so very large or small images remain navigable.

Use `await SetDisplayRangeAsync(range)` to change the requested grayscale mapping
and observe rendering failures. `DisplayRange` reads that request; snapshots retain
the last successfully committed mapping. A newer request or closure cancels an
outstanding update. Without a current frame the call configures future submissions
and completes immediately. See [display contracts](frame-pipeline.md#submission-and-display).

## Measurement notifications and extensions

See [measurements](measurements.md) for completion/removal/change ordering, immutable
snapshots, geometry/query compatibility and fresh tool sessions. Completion does not
promise query readiness. Notifications run on the viewer STA; do not block them.

## Tool registration lifetime

`RegisterMeasurementTool` returns an `IDisposable` handle for one registration.
Component cleanup disposes that handle; viewer-wide management may instead call
`UnregisterMeasurementTool(id)` to remove the current registration under an ID.
Revocation cancels that registration's active session, preserves completed measurements,
and never disposes the caller-owned tool. Handles are idempotent and safe across threads
and after closure. A retained old handle cannot remove a same-ID replacement.
See [registration ownership](measurements.md#tool-registration-ownership) for callback,
activation and captured-menu behavior.

## Internal implementation

Built-in measurement tool implementations are internal; use
`ActivateMeasurementTool(MeasurementToolIds.Point)` (or another built-in ID) to activate them.

`ViewerHost`, `ViewerWindow`, `MenuManager`, `ViewerMenuController`, WPF image/overlay/HUD
layer controls, control-point visuals, measurement edit sessions, `MeasurementItem`, scheduling and
rendering internals are not public contracts. `DrawingElement` is a closed family of
supported drawing descriptions, not a custom-renderer base class. WPF shape factories and
visual metadata are internal. Use drawing descriptions and drawing handles for markers, and measurement models for editable geometry and query results;
consumers must not parse WPF visual trees to observe measurement state.

The pixel HUD controller, built-in save-menu item and shape metadata/cache are internal.
Custom menus use `IMenuItem` or the action-based menu helpers; custom measurements
use `CreateMeasurement`. Built-in save actions receive snapshot services directly.
Menu visibility is determined by `IMenuItem.IsVisible`, evaluated on each opening.
Action-based helpers accept an optional visibility predicate. Use `SeparatorMenuItem`
for separators; the renderer removes leading, repeated and trailing separators.
`MenuItem` and `CheckableMenuItem` require a nonempty, non-whitespace header and
non-null action (and checked-state getter where applicable); constructors reject
invalid arguments before registration. Valid headers are preserved as supplied.
Built-in interaction actions capture their target when the menu opens. Cancel Measurement
only cancels that activation; a replacement activation, including the same tool, is unaffected.

## Menu registration lifetime

`RegisterMenuItem(IMenuItem)` returns an `IDisposable` registration handle. Dispose it from
any thread to revoke only that registration; disposal is idempotent and safe after viewer
closure. Registering the same object twice creates independent registrations. Ignoring the
handle keeps the registration alive until the viewer closes. The viewer never disposes the
caller-owned menu object itself.

Revocation prevents new callbacks from an already open menu and disables its current item;
the next opening omits it. An executing action is allowed to finish, including when it
revokes itself or the viewer closes. `IMenuItem.ExecuteAsync()` returns a `ValueTask`
and starts on the viewer STA; no WPF event objects cross this interface. Bindings await
completion and log failures. The same menu object cannot execute again while its action
is in flight, including after reopening; other actions remain available. Action helpers
accept either `Action` or `Func<ValueTask>`. Async work that outlives the viewer must own
its resources and avoid depending on the viewer dispatcher after it closes.

Visibility/check callbacks run on the viewer STA and may change registrations;
registrations added while building the menu appear on the next opening. Do not block these
callbacks on another thread that is waiting for a viewer operation.

Normal menu closure preserves the pending click until input drains. Explicit revocation or
viewer shutdown invalidates it immediately. If menu construction fails after freezing a
frame, the viewer resumes submissions, removes partial bindings and closes that opening.
Header, visibility and check-state failures are logged at the WPF event boundary; they do
not escape into the dispatcher. A later opening evaluates registrations again.
Viewer shutdown releases registrations, callbacks and WPF menu bindings.

```csharp
using var registration = viewer.RegisterMenuItem(
    new Fizzy.ImageViewer.Menus.MenuItem("Inspect target", InspectTarget));
// Keep the registration for the lifetime of this feature.
```

## Layer capabilities

`Layers.Markers` and `CreateDrawingLayer` return `DrawingLayer`, which supports `Add(DrawingElement)`,
`Add(IEnumerable<DrawingElement>)` and `DrawingClicked`. Both creation overloads and
the image-coordinate `Viewer.Draw*` helpers return `DrawingHandle`, with symmetric
`Replace` overloads for one element or a collection. Replacement changes the whole
content while preserving handle identity and stacking order; shape and element count
may change. Click events identify the whole handle through `DrawingClickedEventArgs.Drawing`.

`Layers.Measurements` returns `MeasurementLayer`; measurement tools create
its content. Both derive from the closed `ViewerLayer` family, which exposes `Name`,
`IsVisible`, `IsHitTestVisible`, `ZIndex` and `Clear`. `Layers.ClearContents` clears content
while preserving the layer collection. `Layers.Items` returns a snapshot of these common
layer handles. Built-in layers cannot be removed; the layer base cannot be
subclassed by consumers.

## API baseline

`tests/Fizzy.ImageViewer.Tests/PublicApi.txt` is the reviewed exported API baseline,
including types, public/protected members, nullability and default arguments. API tests
write `PublicApi.actual.txt` beside the test assembly. Review intentional differences,
update consumers and this contract, then copy that output to the baseline before committing.
