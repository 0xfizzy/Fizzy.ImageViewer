# Snapshots

[简体中文](zh-CN/snapshots.md)

`CaptureSnapshotAsync` captures the committed image, not the newest submission. Whole-frame and region overloads return an independently owned `ImageSnapshot`; dispose it after use.

## Capture and save

`Raw` preserves original values and requires TIFF. `Display` applies the captured committed mapping and supports TIFF, PNG, JPEG and BMP. JPEG composites transparency onto black. Drawings, measurement labels and HUD are not image pixels.

```csharp
using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
await snapshot.SaveAsync(path, SnapshotEncoding.Png);
```

See the [complete export example](../samples/MeasurementsAndExport/Program.cs).

A region must be nonempty and wholly inside the frame. Missing input or full capacity raises `InvalidOperationException`. At most two captures are admitted per Viewer, with one export read at a time; capacity rejection happens before lease acquisition. Acceptance synchronously fixes the target, so queueing never selects a newer frame.

## Ownership, cancellation and failure

Accepted capture owns dependencies needed to finish after Viewer closure. The caller owns the result. `AcquirePixels()` returns a separate lease that may outlive snapshot and Viewer. Its coordinates are region-local; `Region` and `SourceFrame` retain original provenance.

Cancellation does not prove native work stopped; operations own safe cleanup. Saving takes an independent lease. Operation and cleanup failures are preserved together, and cleanup can fail after destination replacement. Check the file before retrying.

Pending or failed display-range changes do not affect captured mapping. See [frame contracts](frame-pipeline.md#freeze-snapshot-and-save) and [menu targets](menus.md).
