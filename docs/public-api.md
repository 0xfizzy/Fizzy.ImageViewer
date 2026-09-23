# Supported public API

`Viewer` constructs an independent STA window. `IViewerAPI` is its complete consumer
contract, including `Label`, `QueryOptions` and `QueryMetrics`. `Viewer` is sealed;
application adapters own an instance and use its public API. Raw-window access is not supported.

| Capability | Supported entry points |
| --- | --- |
| Window | `Show`, `Hide`, `Minimize`, `IsVisible`, `IsMinimized`, title, bounds, borderless mode, `CanUserClose`, `Closed` |
| Lifetime | `DisposeAsync` |
| Frames | `SubmitFrameAsync`, `AcquireCurrentFrame`, `FrameCommitted`, frame descriptors, leases and submission results |
| Pixels | CPU readers, external `IFramePixelSource`, query data and display range |
| Snapshots | `CaptureSnapshotAsync`, `ImageSnapshot` and snapshot encodings |
| Drawing | `Layers`, `ViewerLayers`, `ViewerLayer`, `DrawingLayer`, drawing elements, batch handles, click events and `Draw*` convenience methods |
| HUD | `Label`, `DrawHudText`, `HudTextHandle` |
| Measurements | instance `MeasurementStyle`, tool IDs, built-in activation, registration/unregistration, start/cancel, query configuration and metrics, completion/change/removal events |
| Extensions | `IMenuItem`, `ICheckableMenuItem`, menu helpers, `IMeasurementTool`, `IMeasurementToolSession`, `IMeasurementToolContext`, `IMeasurement`, `MeasurementGeometry`, `MeasurementOptions`, `MeasurementResult` |

The root namespace contains `Viewer` and `IViewerAPI`. Shared `ViewerLayers` and `ViewerLayer` handles belong to `.Layers`. Drawing descriptions,
`DrawingLayer`, batch handles and drawing enums belong to `.Drawing`; measurement tools, models, `MeasurementStyle`,
`MeasurementLayer` and notifications belong
to `.Measurements`; menu contracts and helpers belong to `.Menus`.

## Threads and window lifetime

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
Construction validates window bounds before starting the STA. If initialization fails,
all created resources are released on their owning thread and background work is drained
before the original exception is rethrown.
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

## Measurement notifications and extensions

`MeasurementCompleted` reports all completed measurements, including custom tools;
previews are excluded. `MeasurementRemoved` reports only previously completed items,
including clear and closure. `MeasurementSnapshot` carries the stable ID, immutable
geometry and owner-maintained geometry version. Completion captures geometry at completion;
removal captures the latest geometry. Circle snapshots include their radius.

`MeasurementChanged` reports geometry edits, successful query publication and result
invalidation for completed items, both built-in and custom. Each event includes `Snapshot`
and `Result`; a null result means no valid query result is currently available. A geometry
change reports its new version and clears the old result. Completion precedes change
notifications; previews do not emit changes. Snapshots and result arrays can be retained
on other threads. Callbacks run on the viewer STA and subscriber failures are isolated.

The event's `IDisposable` handle removes the item and its resources from any thread,
including after closure. Removal may occur reentrantly during a completion subscriber.
Subscriber failures are logged and isolated. Completion does not promise pixel-query readiness.

Custom and built-in tool registrations implement `IMeasurementTool.CreateSession(context)`.
Every activation must return a fresh `IMeasurementToolSession`; its `OnClick`, `OnMouseMove`
and `Cancel` callbacks retain that context and keep all temporary interaction state in the session.
Registrations may be shared across viewers when their configuration and factory support concurrent
calls on those viewers' STAs. Sessions and contexts must never be shared between activations.
Tools use `IMeasurementToolContext.CreateMeasurement(geometry, options)`.
The context belongs to one creation session. Once that session completes, is cancelled or
is replaced, creating another preview through its retained context throws `ObjectDisposedException`.
The returned `IMeasurement` owns geometry, display, queries and registered resources.
Update it with `UpdateGeometry`, subscribe to `GeometryChanged` for edit writeback, and
observe `ResultChanged` for immutable query results or invalidation. `Complete` retains
a preview; unfinished items are cleaned when creation ends or is cancelled. Tool callbacks
and measurement operations use the viewer STA. Only event removal handles marshal disposal.

Geometry is a closed family identified by `MeasurementKind`: point, crosshair, line,
rectangle and circle. `MeasurementGeometry.Bounds` is the normalized image-space bounding
rectangle. Lines retain their endpoint order; circles use diameter bounds; points and
crosshairs have zero extent, excluding their screen-space marker size. Query options
compose existing point-pixel, line-profile and rectangle-statistics capabilities; unsupported
combinations fail on creation. The framework owns primary visuals, labels and control points.
Arbitrary WPF attachment, geometry implementations and query algorithms are not extension
contracts. See [measurement contracts and example](measurements.md).

During normal operation, a new session started synchronously from a callback takes
precedence over the interrupted session. Once disposal begins, `StartMeasurement` throws
`ObjectDisposedException` and measurement creation is rejected.

## Internal implementation

Built-in measurement tool implementations are internal; use
`StartMeasurement(MeasurementToolIds.Point)` (or another built-in ID) to activate them.

`ViewerHost`, `ViewerWindow`, `MenuManager`, `ViewerMenuController`, WPF image/overlay/HUD
layer controls, control-point visuals, measurement edit sessions, `MeasurementItem`, scheduling and
rendering internals are not public contracts. `DrawingElement` is a closed family of
supported drawing descriptions, not a custom-renderer base class. WPF shape factories and
visual metadata are internal. Use drawing descriptions and batch handles for markers, and measurement models for editable geometry and query results;
consumers must not parse WPF visual trees to observe measurement state.

The pixel HUD controller, built-in save-menu item and shape metadata/cache are internal.
Custom menus use `IMenuItem` or the action-based menu helpers; custom measurements
use `CreateMeasurement`. Built-in save actions receive snapshot services directly.
Menu visibility is determined by `IMenuItem.IsVisible`, evaluated on each opening.
Action-based helpers accept an optional visibility predicate. Use `SeparatorMenuItem`
for separators; the renderer removes leading, repeated and trailing separators.
Built-in interaction actions capture their target when the menu opens.

## Menu registration lifetime

`RegisterMenu(IMenuItem)` returns an `IDisposable` registration handle. Dispose it from
any thread to revoke only that registration; disposal is idempotent and safe after viewer
closure. Registering the same object twice creates independent registrations. Ignoring the
handle keeps the registration alive until the viewer closes. The viewer never disposes the
caller-owned menu object itself.

Revocation prevents new callbacks from an already open menu and disables its current item;
the next opening omits it. An executing callback is allowed to finish, including when it
revokes itself. Visibility/check callbacks run on the viewer STA and may change registrations;
registrations added while building the menu appear on the next opening. Do not block these
callbacks on another thread that is waiting for a viewer operation.

Normal menu closure preserves the pending click until input drains. Explicit revocation or
viewer shutdown invalidates it immediately. If menu construction fails after freezing a
frame, the viewer resumes submissions and removes partial bindings before propagating the
failure. Viewer shutdown releases registrations, callbacks and WPF menu bindings.

```csharp
using var registration = viewer.RegisterMenu(
    new Fizzy.ImageViewer.Menus.MenuItem("Inspect target", InspectTarget));
// Keep the registration for the lifetime of this feature.
```

## Layer capabilities

`Layers.Markers` and `CreateLayer` return `DrawingLayer`, which supports `AddBatch` and
`BatchClicked`. `Layers.Measurements` returns `MeasurementLayer`; measurement tools create
its content. Both derive from the closed `ViewerLayer` family, which exposes `Name`,
`IsVisible`, `IsHitTestVisible`, `ZIndex` and `Clear`. `Layers.Items` returns a snapshot of
these common layer handles. Built-in layers cannot be removed; the layer base cannot be
subclassed by consumers.

## API baseline

`tests/Fizzy.ImageViewer.Tests/PublicApi.txt` is the reviewed exported API baseline,
including types, public/protected members, nullability and default arguments. API tests
write `PublicApi.actual.txt` beside the test assembly. Review intentional differences,
update consumers and this contract, then copy that output to the baseline before committing.
