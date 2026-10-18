# Viewer and lifetime

[简体中文](../zh-CN/public-api.md)

Configure a Viewer window and own its shutdown. `Viewer` is sealed; application adapters compose an instance rather than access its raw WPF window. [Getting started](getting-started.md) has a complete program that waits for user closure.

`IViewer` contains host-independent capabilities and asynchronous disposal. `Viewer` and `ImageViewerControl` implement it; window operations require `IViewerWindow`, implemented only by `Viewer`. For embedded lifetime and host shutdown, see [embedding](embedding.md).

## Configure and show

```csharp
await using var viewer = new Fizzy.ImageViewer.Viewer(showWindow: false);
viewer.Title = "Inspection";
viewer.HudLabelText = "Ready";
viewer.Show();
// Keep the viewer alive for the owning feature's lifetime.
```

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

### Draining work and independent leases

Disposal drains admitted submissions through release and public completion, including replacement cleanup on submitting threads and frozen/cancelled drops. A pixel source that ignores cancellation can delay shutdown until its work releases its leases. Programmatic disposal is not blocked by `CanUserClose` or a cancelling window-closing handler. Cleanup failures from owned query leases and menu pins are retained in the disposal result; ordinary query/provider failures remain query failures.

Once disposal begins, frame acquisition, snapshot capture, registration and layer operations reject new work with `ObjectDisposedException`; submission consumes ownership and returns `Closed`. Previously acquired leases and snapshots remain independently owned. Viewer does not wait for external leases or reclaim their storage early. Accepted capture/export work with independent dependencies may finish after closure.

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
    CancellationToken cancellationToken = default) => sink.SubmitFrameAsync(frame, cancellationToken: cancellationToken);
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

## Related capabilities

- [Measurements](measurements.md): interaction, immutable results and notification order.
- [Custom tools](measurement-tools.md): registration and one-use creation sessions.
- [Menus](menus.md): registration, target capture and asynchronous execution.
- [Drawing and HUD](drawing-layers.md): layers and individual handles.

The [API reference](../../artifacts/docs/api/index.md) lists supported symbols. Internal WPF controls and visual trees are not extension points. See [architecture](architecture.md) for implementation boundaries and the reviewed API baseline.
