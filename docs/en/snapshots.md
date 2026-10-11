# Snapshots

[简体中文](../zh-CN/snapshots.md)

Capture the committed whole image or a region and save it independently of the Viewer. This exports image pixels, not a viewport screenshot or overlays.

## Capture and save

The snippets assume a live `viewer` and an output `path` whose contents may be replaced:

```csharp
using Fizzy.ImageViewer.Snapshots;

using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
await snapshot.SaveAsync(path, SnapshotEncoding.Png);
```

See the [complete export example](../../samples/MeasurementsAndExport/Program.cs).

## Target and ownership

A region must be nonempty and wholly inside the frame. Missing input raises `InvalidOperationException`. Accepted capture owns the dependencies needed to finish after Viewer closure. Dispose the returned snapshot; `AcquirePixels()` obtains an independent lease that may outlive both snapshot and Viewer. Its coordinates are region-local; `Region` and `SourceFrame` retain original provenance. Pending or failed display-range changes do not affect the captured committed mapping.

`CaptureSnapshotAsync(Raw|Display)` captures the current committed frame at call time and
returns independent immutable storage. `Raw` preserves input values; `Display` applies the
committed display mapping without annotations, HUD or viewport scaling.
The overload accepting PixelRegion reads only that region. Snapshots record `SourceFrame` and
region origin. Full snapshots explicitly request the entire region; GPU results are never cached
back into source storage. One export read per Viewer executes at a time, with at most one
additional capture admitted to wait. Further calls throw `InvalidOperationException`
synchronously before acquiring a frame lease. Accepted captures retain the frame current
at the call, including while waiting. Completion, cancellation or failure frees the slot.
The ROI save menu pins both frame and integer region before opening the file dialog.
Dispose snapshots when finished. A save retains its own lease until completion.
Raw capture takes ownership of the independently read CPU region without copying it
again. That region's release callback runs when the snapshot's final pixel reference
is released, including independent pixel leases retained after snapshot disposal.
Capture cleans up a candidate snapshot if another owned resource fails to
release before delivery; operation and cleanup failures are preserved together.

## Encoding, cancellation and failure

`ImageSnapshot.SaveAsync(path, encoding, token)` writes a temporary file in the destination
directory and replaces the target only after successful encoding. Cancellation is checked
before replacement and between TIFF rows; WPF PNG/JPEG/BMP encoding itself is not interruptible.
Display exports support PNG, JPEG, BMP and TIFF. JPEG composites transparency onto black.
Raw exports require TIFF and never silently quantize high-bit-depth values.
Encoding/cancellation and cleanup failures are preserved together, with the operation failure
first. Cleanup can fail after the target was successfully replaced; a failed save task does
not by itself prove that the destination is unchanged.
The context menu's **Save Image As...** action selects display PNG/JPEG/BMP or raw TIFF
through the file dialog's file type. **Save Region Image As...** offers the same choices
when a completed rectangular measurement is selected.

TIFF uses uncompressed Classic TIFF, UINT Gray16 or IEEEFP Gray32Float, top-left orientation,
RGB channel order and associated/unassociated alpha tags. Bgr32 padding is omitted. Images
exceeding the conservative Classic TIFF size bound are rejected; BigTIFF is not implemented.
The context menu allows one save at a time. Independently created snapshot objects may be saved
concurrently by application code. Captured snapshots remain usable after closing the viewer.

Cancellation does not prove native work stopped; operations retain their dependencies through safe completion. Operation and cleanup failures are preserved together. See [menu targets](menus.md#captured-image-targets).
