# Architecture

Use this guide when changing resource ownership or module dependencies. The library
owns an independent WPF STA per Viewer; callers submit immutable frames and use the
public facade, drawing handles and measurement scopes.

## Source organization

| Directory | Responsibility |
| --- | --- |
| Viewer | Public facade, component composition, window and lifetime |
| Frames | Immutable storage, leases, frame submission queue and committed state |
| Rendering | CPU preparation/presentation and D3D surface presentation |
| Imaging | Original-pixel access, regions, query results and display conversion |
| Imaging/Queries | Shared query protocol, scheduler and execution runtime |
| Drawing | Batch layers, drawing descriptions, styles, HUD handles and shape helpers |
| Measurements | Tool protocols, registry, sessions, geometry and resource owners |
| Interaction | Selection and input-state decisions |
| Editing | Supported shape editors and individual drag sessions |
| Controls | WPF display surfaces and coordinate transforms |
| Snapshots | Captured frame/region ownership and encoding |
| Menus | Menu contracts, menu construction and built-in save actions |

Files belong to their feature, including interfaces and enums. Public namespaces
describe caller-facing API groups; internal namespaces follow component ownership.
Visibility is enforced by C# access modifiers and the reviewed public API baseline.
Viewer partial files organize one facade; they are not independently owned services.

## Ownership and direction

Viewer creates and disposes the frame pipeline, presentation resources, shared pixel
query scheduler, measurement manager, interaction coordinator and HUD. Startup failure
uses the same cleanup owners as normal closure. Disposal waits for outstanding frame
and query work and the actual STA exit.

The interaction coordinator subscribes to input and drawing-layer lifecycle events.
It alone decides selection, editing and measurement transitions. Display controls do
not call it back through stored controller references. Generic drawing layers manage
visibility, hit testing, batches and clear notifications; the window mounts the WPF
measurement overlay into its layer.

Measurement context owns items and custom scopes, and borrows query scheduling.
The pixel HUD independently subscribes to that same scheduler. Query protocol and
runtime are internal imaging capabilities, not public measurement extension points.
The runtime controls time, worker execution and UI publication for deterministic tests.

Built-in editing uses a closed factory for the supported shapes. Point and crosshair
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
