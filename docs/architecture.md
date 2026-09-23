# Architecture

Use this guide when changing resource ownership or module dependencies. The library
owns an independent WPF STA per Viewer; callers submit immutable frames and use the
public facade, drawing handles and measurement handles.

## Source organization

| Directory | Responsibility |
| --- | --- |
| Viewer | Public facade, runtime composition, window and lifetime |
| Frames | Immutable storage, leases, frame submission queue and committed state |
| Rendering | CPU preparation/presentation and D3D surface presentation |
| Imaging | Original-pixel access, regions, query results and display conversion |
| Imaging/Queries | Shared query protocol, scheduler and execution runtime |
| Drawing | Batch layers, drawing descriptions, styles, HUD handles and shape helpers |
| Measurements | Tool protocols, registry, geometry and resource owners |
| Interaction | Interaction session ownership, selection and WPF input binding |
| Editing | Supported shape editors and individual drag sessions |
| Controls | WPF display surfaces and coordinate transforms |
| Snapshots | Captured frame/region ownership and encoding |
| Menus | Menu contracts, menu construction and built-in save actions |

Files belong to their feature, including interfaces and enums. Public and internal namespaces follow feature ownership; only the Viewer facade
and its lifetime/composition helpers live in the root namespace. Built-in tools and
their measurement presentation live together under Measurements/BuiltIn.
Visibility is enforced by C# access modifiers and the reviewed public API baseline.
Viewer partial files organize one facade; they are not independently owned services.
The facade is sealed. Application adapters own a Viewer instance, create it hidden
when setup must precede display, and converge closure and disposal on their own
idempotent cleanup before awaiting the viewer's disposal completion.

## Ownership and direction

Viewer exposes the complete public facade and raises public notifications. Its
ViewerRuntime owns the STA, window, frame pipeline, presentation resources, shared pixel
query scheduler, measurement context, tool registry, interaction coordinator and HUD.
The facade assigns the runtime before starting its STA. Startup failure uses the same
idempotent cleanup entry as normal closure; each owner is cleaned even if another fails.
Disposal waits for outstanding frame and query work and the actual STA exit.
ViewerLifetime provides the shared stopping gate. HudTextCollection owns HUD text
visuals and invalidates their handles on shutdown.

ViewerInputBinding translates WPF events and coordinates, and applies cursor, focus
and capture effects. It holds no session state. The interaction coordinator alone owns
the active tool, session version, mode and selection, and decides editing and measurement
transitions. MeasurementToolRegistry only stores registrations. All tools receive the same public
measurement context when the coordinator executes their callbacks. Display controls do not call controllers
through stored references. Generic drawing layers manage
visibility, hit testing, batches and clear notifications; the window mounts the WPF
measurement overlay into its layer.

Measurement context owns one set of model-driven items and their visual ownership mappings,
and borrows query scheduling. The runtime creates and closes it independently of the
tool registry. The edit manager receives only a measurement lookup delegate.
The pixel HUD independently subscribes to that same scheduler. Query protocol and
query runtime are internal imaging capabilities, not public measurement extension points.
The query runtime controls time, worker execution and UI publication for deterministic tests.

All measurement editing uses a model target for the supported geometries. Point and crosshair
share anchor editing. A drag session owns its initial geometry and release boundary;
there is no dynamic editor registration contract.

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
