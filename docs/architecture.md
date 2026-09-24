# Architecture

Use this guide when changing resource ownership or module dependencies. The library
owns an independent WPF STA per Viewer; callers submit immutable frames and use the
public facade, drawing handles and measurement handles.

## Source organization

| Directory | Responsibility |
| --- | --- |
| Viewer | Public facade, host composition, window and lifetime |
| Frames | Immutable storage, frame descriptors, leases and submission contracts |
| Rendering | Submission queue, committed display state, CPU preparation/presentation and D3D presentation |
| Imaging | Original-pixel access, regions, query results and display conversion shared by rendering and snapshots |
| Imaging/Queries | Shared query protocol, scheduler and execution runtime |
| Layers | Public layer composition and the container for transforms, input and lifetime |
| Drawing | Drawing elements, marker layers, drawing handles and batch rendering |
| Measurements | Measurement handles, collection, lifecycle, options, snapshots, notifications and style |
| Measurements/Queries | Measurement query kinds, geometry compatibility, request construction, query clients and results |
| Measurements/Geometry | Closed immutable geometry family and geometry kinds |
| Measurements/Tools | Tool contracts, registrations and creation contexts |
| Measurements/Tools/BuiltIn | Built-in tools and their shared creation session |
| Measurements/Editing | Measurement edit sessions and control-point interaction |
| Measurements/Presentation | Overlay, visual metadata, shape projection, labels and line-profile windows |
| Interaction | Measurement interaction coordination, WPF input binding and shared mouse capture |
| Viewport | ImageViewport, image-coordinate transforms, zoom and viewport pan |
| Hud | Screen-space surface, text handles, pixel sampling and display state |
| Snapshots | Captured frame/region ownership and encoding |
| Menus | Menu contracts, actions, registration, WPF bindings and captured menu targets |

### Placement rules

Place code with the feature that owns its behavior and lifetime, including interfaces,
enums and controls. Shared code stays with its owning feature; a second caller does not
justify a generic Controls, Models, Services or Helpers directory. Display conversion
belongs to Imaging because both presentation and snapshot encoding use it.

Each public top-level type has a same-named file. Independent internal implementations
and contracts follow the same rule. Keep private implementation types nested in their
owner. The small internal request and result families stay together in QueryRequest.cs
and QueryResult.cs so their alternatives can be read as one protocol. Do not split
nested helper types or create a directory for a single type merely to satisfy a pattern.

Subdirectories group established responsibilities, rather than visibility or type kind.
Measurements/Geometry, Measurements/Tools and Measurements/Queries retain the public Measurements namespace:
source navigation does not require a different consumer namespace for each subgroup.
Built-in tools use Measurements.BuiltIn; editing and presentation have their own internal
namespaces. Viewer files use the root namespace; other top-level feature directories use
their feature namespace. Namespace changes require a separate API decision.

Tests belong to the capability under test, not its historical implementation location.
Rendering tests cover presentation; Viewport tests cover pan; Imaging/Queries tests cover
scheduling. Measurement geometry, query, tool-session and presentation tests use matching
subdirectories. Tests spanning measurement ownership, notifications or interaction stay
at the Measurements level. Test namespaces remain stable so existing filters continue
to work. Test-only helpers live beside their callers; benchmarks remain under tools.

Keep the single library project at the repository root. Viewer partial files organize
one facade by capability: Window, Frames, Display, Drawing, Measurements, HUD, Queries,
Snapshots and Lifetime. These files are not independently owned services. `IViewer` aggregates
borrowed capability interfaces placed with their owning features. `IViewerWindow` stays
with Viewer; display control belongs to Viewport, shared query settings to Imaging.
`Viewer` implements these interfaces directly, with one shared lifetime; only the aggregate
contract includes asynchronous disposal and global `Layers` composition. Drawing exposes
only its default `Markers` layer; measurements expose their `Measurements` layer.
Layer handles manage the entire corresponding layer, while drawing/HUD/registration
handles manage individual resources. Review returned objects, collection elements and
event/callback arguments as well as direct members when changing a capability boundary.
These interfaces constrain typed dependencies; a shared implementation and event sender
do not provide security isolation. Documentation in
docs describes current component contracts; plans/archive contains historical plans.
Update this map when introducing or removing a feature boundary, not for ordinary files.

Visibility is enforced by C# access modifiers and the reviewed public API baseline.
Integration tests access composed internals through Viewer.Host; component tests construct
their owners directly. Viewer has no menu-freeze or snapshot-target forwarding methods.
Viewer.Queries contains the shared pixel-query configuration; Viewer.Hud contains screen-space text APIs and pixel inspection settings.
The facade is sealed. Application adapters own a Viewer instance, create it hidden
when setup must precede display, and converge closure and disposal on their own
idempotent cleanup before awaiting the viewer's disposal completion.

## Ownership and direction

Viewer exposes the complete public facade and raises public notifications. Host composes
frame notifications in submission-callback, public-event order;
subscriber failures are isolated at each boundary.
ViewerHost owns the STA, window, frame pipeline, presentation resources, shared pixel
query scheduler, measurement collection, tool registry, interaction coordinator and HUD.
The facade assigns the host before starting its STA. Startup failure uses the same
idempotent cleanup entry as normal closure; each owner is cleaned even if another fails.
Disposal waits for outstanding frame and query work and the actual STA exit.
ViewerLifetime provides the shared stopping gate and shutdown-safe STA removal dispatch for measurement, drawing and HUD handles. HudTextCollection owns HUD text
visuals and invalidates their handles on shutdown.

ViewerInputBinding resolves WPF event sources through the measurement collection and
passes measurement identities and image coordinates to the coordinator. It implements
the small IInteractionView boundary for selection appearance, cursor, focus, capture,
image scale and temporary layer input suppression without owning tool-session state.
The adapter borrows LayerCollection; the coordinator borrows MeasurementLayer directly
for admission policy and lifecycle events, without depending on ViewerLayers or its container.
ViewerHost connects the input adapter to the coordinator; the coordinator depends on these effects rather than concrete WPF visuals
or the input adapter. ViewportPan owns middle-button
pan state in screen coordinates;
it and measurement editing use MouseCaptureSession for capture admission, loss and
idempotent release. Failed capture never starts a drag. Cancellation, interaction
switching, hiding/unloading the image surface and shutdown end viewport capture.
The interaction coordinator alone owns
the active tool, session version, mode and selected measurement, and decides editing and measurement
transitions. Tool and edit admission share the layer's configured visibility and hit-test
policy, including admission after reentrant cancellation. Temporary input suppression
does not change that configured policy. MeasurementToolRegistry stores reusable tool registrations and their metadata. On each activation,
`IMeasurementTool.CreateSession(context)` returns a fresh `IMeasurementToolSession` containing
that activation's mutable state. The coordinator disposes each callback session once on every terminal path and owns its explicit
`MeasurementCreationContext` context; MeasurementCollection stores no ambient current session.
Every preview must be created through its owning context. Ended contexts reject creation,
including from callbacks interrupted by a newer session. An activation aggregates its
registration identity, creation context and callback session; callback reentry validates
that activation before continuing. Context-scoped finishing cannot affect replacements.

MeasurementCollection owns membership, visual lookup, creation policy and viewer style.
ViewerHost constructs MeasurementRuntime and shares it with the collection and interaction
coordinator. The runtime supplies handle and creation-context STA dispatch, query registration
and shared notification execution; MeasurementItem receives the runtime, presentation layer
and style explicitly. Creation contexts receive the runtime and a frame-acquisition delegate
directly, while the collection owns creation and membership rather than forwarding execution
or frame access. Collection shutdown stops the shared runtime before disposing its items.
A per-viewer MeasurementNotificationQueue
serializes immutable viewer and handle notifications, including mutations made reentrantly
by subscribers. Internal geometry application remains synchronous so editing controls are
consistent before any public notification is queued.

Display controls do not call controllers through stored references. `ViewerLayer` owns common visibility, hit testing and clear policy.
`ViewerLayers` composes default layers and exposes consumer capabilities. Its internal
`LayerCollection` owns attachment, transforms, input suppression and lifecycle without
depending on concrete drawing or measurement types. Concrete layers depend on that
container rather than the public composition facade.
`DrawingLayer` owns drawings with one or many elements through the same `DrawingHandle`; `MeasurementLayer` owns the WPF measurement overlay and exposes
a content-clearing notification. MeasurementCollection subscribes to clear its owned items and
unsubscribes on shutdown; the layer has no reference to the concrete collection. Clear enters the
layer's clearing gate, cancels input and selection, then always notifies content cleanup and
removes remaining visuals in finally blocks. Model, query and resource cleanup does not depend on a coordinator being present.
The same layer gate rejects creation and interaction starts throughout cancellation and cleanup.
MeasurementLayer exposes no batch creation or batch-click events.

MeasurementItem implements the public IMeasurement handle with STA dispatch and owns
model state, query subscription and disposal. Completion and disposal release its creation-context
reference before presentation or external callbacks; only immutable Origin survives as session provenance.
The item directly disposes its presentation after deregistration and before removal notification. MeasurementPresentation owns its WPF
visuals, their attachment/detachment, labels and optional plot. Plot closure requests item disposal, and active disposal
detaches that callback before closing the plot.
MeasurementQueryDefinition owns the supported query/geometry pairs and request construction. MeasurementQueryClient caches those requests independently of the handle. Each request
captures its geometry version and coordinates for immutable result provenance.
MeasurementRuntime owns ViewerLifetime and Dispatcher access and registers measurement
query clients with the shared scheduler; model dispatch and shutdown admission do not
depend on the visual container. MeasurementCollection owns a primary set of model-driven
items and a separate visual lookup index. The host creates and closes the collection
independently of the tool registry. Hit testing resolves a visual to its registered measurement before selection.
The measurement edit controller receives the measurement directly; unregistered visuals cannot be selected, edited or deleted.
The pixel HUD independently subscribes to that same scheduler. Query protocol and
query runtime are internal imaging capabilities, not public measurement extension points.
A validated query publishes frame identity and its payload in one synchronous call;
clients never stage samples awaiting a second publication notification.
The query runtime controls time, worker execution and UI publication for deterministic tests.
LineSampling computes clipped sample coordinates independently of display. The optional plot
consumes immutable MeasurementSampleResult samples and owns its channel buffers; data-only line
queries allocate no plot or intermediate RGB buffers.

MeasurementEditController directly owns a MeasurementEditSession and its control-point visuals.
The session observes internal geometry application before public notifications and refreshes
control points for both pointer and programmatic updates. It preserves the drag-start fixed
anchor across corner crossing; an external update during a drag replaces that baseline.
Geometry operations belong to the closed MeasurementGeometry family, with one file per concrete geometry. Capability checks do not create sessions.
There is no editor factory or dynamic editor registration contract.

MenuManager owns registrations and WPF click bindings. Each registration has an
independent disposable handle; revocation disables current bindings and releases their
targets. Normal menu closure retains bindings until input drains because WPF can deliver
Closed before Click. A new opening invalidates the previous bindings and delayed cleanup.
Menu bindings await `ExecuteAsync`, isolate failures through logging and prevent
concurrent execution of the same menu object across openings. Started work owns its
resources and may outlive registration or viewer closure.
ViewerMenuController supplies built-in menu policy and captures interaction/ROI targets,
borrowing interaction, tools, layers, pixel HUD and snapshot services. MenuSnapshotSession in Menus
alone owns frozen frame/ROI leases. Snapshots supplies capture and encoding without menu policy.
ViewerHost detaches menu policy and disposes menu bindings before stopping the pipeline and disposing snapshot and interaction resources.

Rendering owns CommittedViewLease, which retains a frame lease together with atomically
captured display range and commit version. Menu targets and snapshots consume this committed
view without owning pipeline state.

## Pixel format ownership

`Frames.FramePixelFormats.GetInfo` supplies the internal immutable `FramePixelFormatInfo`
for each supported format. Component order, component bit depth, numeric representation
and alpha mode are defined there; storage size, grayscale classification and semantic
channel count are derived from those facts. Public `BytesPerPixel` uses the same definition.

Imaging owns byte decoding, statistics and display mapping. Rendering owns WPF format
adaptation; Snapshots owns output layout, channel conversion and encoding tags. These
components consume format facts while retaining their own algorithms and policies:
display ranges are not source-value limits, plot channel selection is not a format channel
count, and encoded row sizes need not equal source row sizes. `RegionStatistics` validates
its own format and channel count; frame leases check that provider results match the source.

When adding a format, define its intrinsic facts once and review decoding, statistics,
display, HUD/measurement presentation and snapshot paths. Each path must support it
explicitly or reject it. A descriptor entry alone does not implement format support.

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
