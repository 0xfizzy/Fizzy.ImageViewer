# Supported public API

`Viewer` constructs an independent STA window. `IViewer` is its complete consumer
contract, including `HudLabel`, `QueryOptions` and `QueryMetrics`. `Viewer` is sealed;
application adapters own an instance and use its public API. Raw-window access is not supported.

| Capability | Supported entry points |
| --- | --- |
| Window | `Show`, `Hide`, `Minimize`, `IsVisible`, `IsMinimized`, title, bounds, borderless mode, `CanUserClose`, `Closed` |
| Lifetime | `DisposeAsync` |
| Frames | `SubmitFrameAsync`, `AcquireCurrentFrame`, `FrameCommitted`, frame descriptors, leases and submission results |
| Pixels | CPU readers, external `IFramePixelSource`, query data, shared query configuration/metrics and display range |
| Snapshots | `CaptureSnapshotAsync`, `ImageSnapshot` and snapshot encodings |
| Drawing | `Layers`, `ViewerLayers`, `ViewerLayer`, `DrawingLayer`, drawing elements, drawing handles, click events and `Draw*` convenience methods |
| HUD | `HudLabel`, `IsPixelInfoEnabled`, `DrawHudText`, `HudTextHandle` |
| Measurements | instance `MeasurementStyle`, tool IDs, built-in activation, registration/unregistration, start/cancel, completion/change/removal events |
| Extensions | `IMenuItem`, `ICheckableMenuItem`, menu helpers, `IMeasurementTool`, `IMeasurementToolSession`, `IMeasurementToolContext`, `IMeasurement`, `MeasurementGeometry`, `MeasurementOptions`, `MeasurementResult` |

The root namespace contains `Viewer` and `IViewer`. Shared `ViewerLayers` and `ViewerLayer` handles belong to `.Layers`. Drawing descriptions,
`DrawingLayer`, drawing handles and drawing enums belong to `.Drawing`; measurement tools, models, `MeasurementStyle`,
`MeasurementLayer` and notifications belong
to `.Measurements`; menu contracts and helpers belong to `.Menus`.
HUD text handles and anchor alignment belong to `.Hud`.

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

`HudLabel` controls the upper-right HUD text. `IsPixelInfoEnabled` defaults to true and
controls bottom-left pixel inspection and its query subscription; the context menu uses
the same state. Both properties can be configured before showing the window.
`EndInteraction()` cancels unfinished creation or ends editing, preserving completed
measurements and edits already applied.

## Measurement notifications and extensions

`MeasurementCompleted` reports all completed measurements, including custom tools;
previews are excluded. `MeasurementRemoved` reports only previously completed items,
including clear and closure. `MeasurementSnapshot` carries the stable ID, immutable
geometry, origin and owner-maintained geometry version. Completion captures geometry at completion;
removal captures the latest geometry. Circle snapshots include their radius.

`MeasurementChanged` reports geometry edits, successful query publication and result
invalidation for completed items, both built-in and custom. Each event includes `Snapshot`
and `Result`; a null result means no valid query result is currently available. A geometry
change reports its new version and clears the old result. Completion precedes change
notifications; previews do not emit changes. Snapshots and result arrays can be retained
on other threads. Callbacks run on the viewer STA and subscriber failures are isolated.

The event's `IMeasurement Measurement` is the same live handle returned to its tool.
Use it to update geometry or dispose the item from any thread; disposal is safe after
closure. Removal may occur reentrantly during a completion subscriber.
Completion does not promise pixel-query readiness.

Viewer and handle notifications share an ordered STA queue. Reentrant mutations append
notifications without reversing versions or delivering changes after removal. Event payloads
are immutable snapshots; their live handles can already reflect later changes. `Closed`
follows queued removal notifications. Explicit calls preserve operation and cleanup failures;
framework input/window callbacks log failures so one tool cannot terminate the viewer.

Tools implement `IMeasurementToolSession.OnClick` with `MeasurementClickResult.Continue`
or `Finish`. `IMeasurement.Complete()` commits one item; `IMeasurementToolContext.Finish()`
ends only the originating activation and is safe to call from asynchronous work. Contexts,
handles and snapshots retain `MeasurementOrigin` (`ToolId`, `SessionId`). `Kind` describes
geometry through `MeasurementGeometryKind`, independently of the originating tool.

`MeasurementOptions.Query` takes a closed `MeasurementQueryOptions` configuration.
Use its `None`, `Pixel`, `LineProfile` and `RegionStatistics` presets, or
`new LineProfileMeasurementQueryOptions(showWindow: true)` for a profile window.
`MeasurementResult` contains common provenance; pattern-match `MeasurementSampleResult`
for coordinate/sample pairs or `MeasurementRegionResult` for region/channel statistics.

Custom and built-in tool registrations implement `IMeasurementTool.CreateSession(context)`.
Every activation must return a fresh `IMeasurementToolSession`; its `OnClick`, `OnMouseMove`
and `Cancel` callbacks retain that context and keep all temporary interaction state in the session.
The framework calls the session's `Dispose` exactly once on the STA after completion,
interruption or callback failure, including a superseded factory's returned session.
The context ends before cancellation/disposal; unfinished measurements are released even
if either callback throws. Session disposal owns temporary subscriptions and timers;
measurement resources instead survive successful completion until the item is removed.
Registrations may be shared across viewers when their configuration and factory support concurrent
calls on those viewers' STAs. Sessions and contexts must never be shared between activations.
Tools use `IMeasurementToolContext.CreateMeasurement(geometry, options)`.
The context belongs to one creation session. Once that session completes, is cancelled or
is replaced, creating another preview through its retained context throws `ObjectDisposedException`.
The returned `IMeasurement` owns geometry, display, queries and registered resources.
Update it with `UpdateGeometry`, subscribe to `GeometryChanged` for edit writeback, and
observe `ResultChanged` for immutable query results or invalidation. `Complete` retains
a preview; unfinished items are cleaned when creation ends or is cancelled. Tool callbacks
and context creation run on the viewer STA. `IMeasurement` reads, updates and subscription
changes synchronously dispatch to that STA. `Dispose` and event unsubscription are safe
after viewer closure; `Id`, `Origin` and `IsDisposed` remain readable. Other access requires a running
viewer, and mutations reject disposed items. Notifications still run on STA: never block
them waiting for worker code that is calling the handle. Retained event snapshots remain
immutable even when another subscriber updates or disposes the live handle.

Geometry is a closed family of immutable records: `PointMeasurementGeometry`,
`CrosshairMeasurementGeometry`, `LineMeasurementGeometry`, `RectangleMeasurementGeometry`
and `CircleMeasurementGeometry`. The `MeasurementGeometry` factories return these concrete
types. Pattern-match the geometry to access `Position`, `Start`/`End` or `Center`/`Radius`;
the base exposes only common `Kind` and `Bounds` properties. `MeasurementGeometry.Bounds` is the normalized image-space bounding
rectangle. Lines retain their endpoint order; circles use diameter bounds; points and
crosshairs have zero extent, excluding their screen-space marker size. Query options
compose existing point-pixel, line-profile and rectangle-statistics capabilities; unsupported
combinations fail on creation. The framework owns primary visuals, labels and control points.
Programmatic geometry updates synchronize editing control points before public notifications; updates during
a drag replace its geometric baseline while preserving the active control-point index.
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
visual metadata are internal. Use drawing descriptions and drawing handles for markers, and measurement models for editable geometry and query results;
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
frame, the viewer resumes submissions and removes partial bindings before propagating the
failure. Viewer shutdown releases registrations, callbacks and WPF menu bindings.

```csharp
using var registration = viewer.RegisterMenu(
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
