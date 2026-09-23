# Supported public API

`Viewer` constructs an independent STA window. `IViewerAPI` is its complete consumer
contract, including `Label`, `QueryOptions` and `QueryMetrics`. Inheritance is allowed,
but there is no protected window or supported raw-window access.

| Capability | Supported entry points |
| --- | --- |
| Window | `Show`, `Hide`, `Minimize`, `IsVisible`, `IsMinimized`, title, bounds, borderless mode, `CanUserClose`, `Closed` |
| Lifetime | `DisposeAsync` |
| Frames | `SubmitFrameAsync`, `AcquireCurrentFrame`, `FrameCommitted`, frame descriptors, leases and submission results |
| Pixels | CPU readers, external `IFramePixelSource`, query data and display range |
| Snapshots | `CaptureSnapshotAsync`, `ImageSnapshot` and snapshot encodings |
| Drawing | `Layers`, `ViewerLayers`, `DrawingLayer`, drawing elements, batch handles, click events and `Draw*` convenience methods |
| HUD | `Label`, `DrawHudText`, `HudTextHandle` |
| Measurements | instance `MeasurementStyle`, tool IDs, built-in activation, registration/unregistration, start/cancel, query configuration and metrics, completion/removal events |
| Extensions | `IMenuItem`, `ICheckableMenuItem`, menu helpers, `IMeasureMethod`, `IMeasureToolContext`, `IMeasurementScope` |

## Threads and window lifetime

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
Internal shutdown and startup rollback never invoke an overridden `DisposeAsync`.
Overrides apply to explicit caller disposal and should await the base implementation;
use `Closed` for application resources that must also be released on user closure.
`Closed` is a window notification, not a substitute for awaiting disposal. There is no
separate public `Close` method. Event callbacks run on the viewer STA; do not block them
waiting for shutdown or for work that needs that dispatcher.

Frame submission retains its explicit ownership and terminal-result contract: submission
after closure consumes the frame and returns `Closed`, rather than using the window
property exception policy. Drawing and HUD handle disposal is idempotent after closure.
See [frame contracts](frame-pipeline.md) and [drawing contracts](drawing-layers.md).

## Measurement notifications and extensions

`MeasurementCompleted` reports completed built-in point, line and rectangle geometry,
including line-strength measurements. It excludes previews and custom scope visuals.
`MeasurementRemoved` reports only previously completed measurements, including clear
and window closure. Removal contains the latest geometry; completion contains the
geometry at completion. Both snapshots share a stable measurement ID.

`MeasurementSnapshot` is immutable image-coordinate data. `Start` is the point position;
line uses both endpoints; rectangle uses normalized opposite corners. The event's
`IDisposable` handle removes the measurement and its resources from any thread and is
safe to dispose repeatedly or after closure. It exposes no geometry mutation API.
Subscribers may remove a measurement during completion; removal can therefore be
delivered reentrantly before later completion subscribers. Subscriber failures are logged
and isolated. Notifications do not promise pixel-query results are already available.

Custom measurement tools own visuals and resources exclusively through
`IMeasurementScope`; scope operations and tool callbacks use the viewer STA.
During normal operation, a session started synchronously from a tool callback or
completion subscriber takes precedence over the interrupted session. Tool callback
exceptions propagate without cancelling a newer session; notification subscriber
exceptions are logged and isolated. Once disposal begins, `StartMeasure` throws
`ObjectDisposedException`.
See [measurement contracts](measurements.md). Arbitrary custom control-point editors
are not a public extension point. Built-in editing remains supported.

## Internal implementation

Built-in measurement tool implementations are internal; use
`StartMeasure(MeasureToolIds.Point)` (or another built-in ID) to activate them.

`ViewerWindow`, `MenuManager`, WPF image/overlay/HUD layer controls, control-point visuals, all shape
editors and their factory, `MeasurementItem`, `MeasurementGeometry`, scheduling and
rendering internals are not public contracts. `DrawingElement` is a closed family of
supported drawing descriptions, not a custom-renderer base class. Public shape helpers
used by custom measurement tools remain available; consumers must not parse their WPF
visual trees or cached transform metadata to observe built-in measurement results.

The pixel HUD controller, built-in save-menu item and shape metadata/cache are internal.
Custom menus use `IMenuItem` or the action-based menu helpers; custom measurement visuals
use `Shapes.Create*` and scopes. Built-in save actions receive snapshot services directly.

`tests/Fizzy.ImageViewer.Tests/PublicApi.txt` is the reviewed exported API baseline,
including types, public/protected members, nullability and default arguments. API tests
write `PublicApi.actual.txt` beside the test assembly. Review intentional differences,
update consumers and this contract, then copy that output to the baseline before committing.
