# Architecture

Use this guide when changing resource ownership or module dependencies. The library
owns an independent WPF STA per Viewer; callers submit immutable frames and use the
public facade, drawing handles and measurement handles.

## Source organization

| Directory | Responsibility |
| --- | --- |
| Viewer | Public facade, host composition, window and lifetime |
| Frames | Immutable storage, leases, frame submission queue and committed state |
| Rendering | CPU preparation/presentation and D3D surface presentation |
| Imaging | Original-pixel access, regions, query results and display conversion |
| Imaging/Queries | Shared query protocol, scheduler and execution runtime |
| Drawing | Shared layer settings, batch drawing descriptions and HUD handles |
| Measurements | Tool protocols, creation sessions, geometry, styles and resource owners |
| Interaction | Interaction session ownership, selection and WPF input binding |
| Editing | Measurement edit sessions and control-point interaction |
| Controls | WPF display surfaces, visual metadata and coordinate transforms |
| Snapshots | Captured frame/region ownership and encoding |
| Menus | Registration ownership, WPF bindings, viewer menu policy and save actions |

Files belong to their feature, including interfaces and enums. Public and internal namespaces
follow feature ownership; only the Viewer facade and its lifetime/composition helpers live
in the root namespace. Built-in tools live under Measurements/BuiltIn.
Measurements/Presentation owns visual creation, geometry projection, labels and line-profile windows.
Visibility is enforced by C# access modifiers and the reviewed public API baseline.
Integration tests access composed internals through Viewer.Host; component tests construct
their owners directly. Viewer has no menu-freeze or snapshot-target forwarding methods.
Viewer partial files organize one facade; they are not independently owned services.
The facade is sealed. Application adapters own a Viewer instance, create it hidden
when setup must precede display, and converge closure and disposal on their own
idempotent cleanup before awaiting the viewer's disposal completion.

## Ownership and direction

Viewer exposes the complete public facade and raises public notifications. Host composes
internal frame notifications in submission-callback, measurement-context, public-event order;
subscriber failures are isolated at each boundary.
ViewerHost owns the STA, window, frame pipeline, presentation resources, shared pixel
query scheduler, measurement context, tool registry, interaction coordinator and HUD.
The facade assigns the host before starting its STA. Startup failure uses the same
idempotent cleanup entry as normal closure; each owner is cleaned even if another fails.
Disposal waits for outstanding frame and query work and the actual STA exit.
ViewerLifetime provides the shared stopping gate. HudTextCollection owns HUD text
visuals and invalidates their handles on shutdown.

ViewerInputBinding translates WPF events and coordinates, and applies cursor, focus
and capture effects. It holds no session state. The interaction coordinator alone owns
the active tool, session version, mode and selected measurement, and decides editing and measurement
transitions. MeasurementToolRegistry only stores registrations. Each tool session receives an `IMeasurementToolContext` that owns its unfinished items.
Ended contexts reject creation, including from callbacks interrupted by a newer session. Display controls do not call controllers
through stored references. `ViewerLayer` owns common visibility, hit testing and clear policy.
`DrawingLayer` owns batches; `MeasurementLayer` owns the WPF measurement overlay and
exposes no batch creation or batch-click events.

MeasurementItem owns model state, queries and disposal; MeasurementPresentation owns its WPF
visuals, their attachment/detachment, labels and optional plot. Plot closure requests item disposal, and active disposal
detaches that callback before closing the plot.
Measurement context owns a primary set of model-driven items and a separate visual lookup index,
and borrows query scheduling. The host creates and closes it independently of the
tool registry. Hit testing resolves a visual to its registered measurement before selection.
The edit manager receives the measurement directly; unregistered visuals cannot be selected, edited or deleted.
The pixel HUD independently subscribes to that same scheduler. Query protocol and
query runtime are internal imaging capabilities, not public measurement extension points.
A validated query publishes frame identity and its payload in one synchronous call;
clients never stage samples awaiting a second publication notification.
The query runtime controls time, worker execution and UI publication for deterministic tests.

EditManager directly owns a MeasurementEditSession and its control-point visuals.
The session captures drag-start geometry and writes through MeasurementItem; geometry
operations live in MeasurementGeometry. Capability checks do not create sessions.
There is no editor factory or dynamic editor registration contract.

MenuManager owns registrations and WPF click bindings. Each registration has an
independent disposable handle; revocation disables current bindings and releases their
targets. Normal menu closure retains bindings until input drains because WPF can deliver
Closed before Click. A new opening invalidates the previous bindings and delayed cleanup.
ViewerMenuController supplies built-in menu policy and captures interaction/ROI targets,
borrowing interaction, tools, layers, pixel HUD and snapshot services. MenuSnapshotSession in Menus
alone owns frozen frame/ROI leases. Snapshots supplies capture and encoding without menu policy.
ViewerHost detaches menu policy and disposes menu bindings before stopping the pipeline and disposing snapshot and interaction resources.

## Invariants when extending the library

- Frame submission consumes ownership immediately; terminal results and last-lease
  release remain explicit, including cancellation, replacement and shutdown.
- CPU display buffers and native GPU surfaces have different presentation lifetimes.
  `ICpuImagePresenter` abstracts only the former.
- Batch marker rendering and individual WPF measurement visuals serve different
  workloads. Keep their ownership and interaction rules explicit.
- Copy and freeze caller brushes before crossing the STA boundary. Measurement styles
  belong to a Viewer; created visuals retain their own appearance snapshot.
- During ordinary tool callbacks the newest session owns input. Bulk clear rejects new
  sessions, is reentrantly idempotent and attempts every layer after a cleanup failure.
- Expose stable consumer capabilities rather than controls, caches or service instances.
  Review API baseline changes together with callers and component documentation.

See [public API](public-api.md), [frame pipeline](frame-pipeline.md),
[measurements](measurements.md), [drawing](drawing-layers.md) and
[validation](validation.md) for the behavioral contracts and verification commands.
