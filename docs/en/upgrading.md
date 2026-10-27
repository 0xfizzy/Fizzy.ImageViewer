# Upgrade an existing application

[简体中文](../zh-CN/upgrading.md)

## From 1.2.0 to 1.3.0

**1.3.0 contains breaking API and behavioral changes.** Update and rebuild consumers;
binaries compiled against 1.2.0 are not guaranteed to work with the new assemblies.
Use the steps below for existing applications. New applications can start with
[getting started](getting-started.md) or [embedding](embedding.md).

### 1. Update package references

Once 1.3.0 is published, update the core package to `Fizzy.ImageViewer` 1.3.0 in your
project or central package version file and restore dependencies. The package includes
`net8.0-windows` and `net10.0-windows`; keep `UseWPF` enabled. Upgrading the library
does not require migrating a .NET 8 application to .NET 10.

If you use the optional CUDA extension, update its references as described in step 5
before restoring. Core and CUDA packages are published separately; a core release
does not mean the matching CUDA packages are already available.

### 2. Update interfaces, events and display settings

`IViewer` no longer includes `IViewerWindow`. Keep `Show`, `Hide`, `Minimize`, window
bounds, title and `Closed` on `Viewer` / `IViewerWindow`. Borrow `IViewerCapabilities`
for shared functionality; retain `IViewer` where the owner needs `DisposeAsync`.
Only standalone `Viewer` implements the window interface.

`FrameCommitted`, `IMeasurement.GeometryChanged` and `IMeasurement.QueryResultChanged`
now use `EventHandler<T>`:

```csharp
// 1.2.0
viewer.FrameCommitted += frame => Console.WriteLine(frame.FrameId);
// 1.3.0
viewer.FrameCommitted += (sender, frame) => Console.WriteLine(frame.FrameId);
```

The sender is the public event owner: `Viewer`, `ImageViewerControl` or the measurement.
Adding library event handlers after termination throws; removing handlers remains safe.
Subscribe before submitting frames. See [event contracts](public-api.md#event-subscriptions).

Display-range updates accept configuration without waiting for display:

```csharp
// 1.2.0
await viewer.SetDisplayRangeAsync(range);
// 1.3.0
viewer.SetDisplayRange(range);
```

Observe subsequent rendering errors through `DisplayFailed`. Snapshots use the last
committed mapping, which can differ from the latest requested range.

### 3. Separate reception from display

`SubmitFrameAsync` now completes reception only. Replace switches using `Committed`,
`Superseded` or `Failed` with `Accepted`, `Cancelled`, `Frozen` and `Closed`.
Check `result.Error` for reception-owned cleanup failures before handling its status:

```csharp
// 1.2.0: completion could report a committed frame.
var result = await viewer.SubmitFrameAsync(frame);
if (result.Status == FrameSubmitStatus.Committed)
    Console.WriteLine("Committed");
```

```csharp
// 1.3.0: completion reports reception, not display.
var result = await viewer.SubmitFrameAsync(frame);
if (result.Error is { } error) throw error;
if (result.Status == FrameSubmitStatus.Accepted)
    Console.WriteLine("Accepted; display may follow");
// Handle Cancelled, Frozen and Closed according to the application's policy.
```

Enum numeric values changed; do not interpret persisted numeric statuses using the
new meanings. **Accepted does not mean displayed and never makes transferred storage
reusable.** Accepted frames may be replaced without a display notification. Cancellation
is checked only before acceptance. Hidden, unloaded or minimized hosts retain the newest
waiting frame and resume display without requiring another submission.

Observe UI commits with `FrameCommitted` or the submission's `OnCommitted` callback,
and rendering failures with `DisplayFailed`. Do not wait unconditionally for every
accepted frame to commit. A commit is not proof of physical monitor presentation.

### 4. Preserve ownership and await shutdown

Submission transfers frame ownership immediately, including rejection and cancellation.
Recycle owned buffers only from their final-release callback, or use `ImageFrame.Copy`
for borrowed/reusable producer buffers. Later cleanup failures for accepted storage
are reported by `DisposeAsync`, not a newer receipt.

`OnCommitted` borrows a lease until callback return. Acquire an independent lease for
later work and dispose it when finished. Independent external leases can outlive the
viewer; the viewer neither waits for them nor reclaims their storage early.

`WhenStopping` signals that admission has closed, not that work has drained. Owners
still await `DisposeAsync` to observe completed cleanup or its shared failure result.
Do not synchronously block the UI thread waiting for disposal.

For embedded use, create `ImageViewerControl` on the host STA and await disposal before
its Dispatcher stops. `Unloaded` ends interaction but preserves content for reattachment
on the same Dispatcher; it is not disposal. Final disposal never closes the host window
or Dispatcher. Create a new instance after final disposal. See the complete
[embedded shutdown example](embedding.md) and [frame ownership](frame-pipeline.md).

### 5. Migrate the optional CUDA package

Remove the former `Fizzy.ImageViewer.Cuda` package reference. Choose exactly one of
`Fizzy.ImageViewer.Cuda11`, `Fizzy.ImageViewer.Cuda12` or `Fizzy.ImageViewer.Cuda13`,
matching the host's CUDA major version, and use version 1.3.0 with core 1.3.0 once
that variant is published. Update central versions and direct/transitive consumers
together; do not combine variants or keep the former package alongside one.

All variants retain the `Fizzy.ImageViewer.Cuda` namespace and assembly name. Each is
a complete package with its Windows x64 native component; no namespace rename is needed.
See [CUDA setup and coverage](cuda.md#build-and-deployment) for toolchain, driver and
architecture requirements. Successful core publication alone does not establish CUDA
package availability or GPU execution coverage.

### 6. Rebuild and verify the affected integration

Rebuild all consumers and adapters against the new references. Check status handling,
event callbacks, buffer recycling and shutdown against the steps above. Run the existing
integration tests covering the paths your application uses, including hidden/resumed
display and embedded shutdown when applicable. Compilation alone cannot check the changed
meaning of submission completion. For repository commands and validation boundaries,
see [validation](validation.md).
