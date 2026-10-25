# Changelog

## 1.3.0 (unreleased)

This candidate contains breaking API and behavioral changes. Update and rebuild
consumers compiled against 1.2.0; binary compatibility is not guaranteed.

### Changes

- Add `ImageViewerControl` for embedding on a WPF host's dispatcher. `Viewer`
  hosts the same implementation on its independent window STA.
- Add borrowed `IViewerCapabilities` and `WhenStopping` observation, plus
  `EmbeddedViewer` and `ApplicationEndpoint` examples with awaited shutdown.
- Separate frame reception from display. Hidden, unloaded and minimized hosts
  retain the latest waiting frame and resume display without requiring new input.
- Clip image, drawing, measurement and HUD content at the owned viewport boundary,
  including after zoom, pan and resize. Pointer input restores focus to the clicked
  viewer so keyboard interaction does not target a neighboring control.
- Add `PixelCoordinate.ToImageCenter()` for converting integer pixel indices to
  continuous image coordinates, and use high-quality image downscaling.
- Include .NET 10 and .NET 8 Windows assemblies and bilingual embedding guidance.

### Upgrade from 1.2.0

- `IViewer` no longer includes `IViewerWindow`. Keep window operations (`Show`,
  `Hide`, `Minimize`, bounds, title and `Closed`) on `Viewer` / `IViewerWindow`.
  Use `IViewerCapabilities` for borrowed shared functionality and retain `IViewer`
  only where ownership requires `DisposeAsync`. `WhenStopping` signals admission
  closure, not completed cleanup; owners still await disposal.
- Change `FrameCommitted`, `IMeasurement.GeometryChanged` and `QueryResultChanged`
  handlers from one argument to `(sender, payload)` (`EventHandler<T>`). The sender
  is the public event owner: `Viewer`, `ImageViewerControl` or the measurement.
  Adding library event handlers after termination now throws; removing remains safe.
- Replace `await SetDisplayRangeAsync(range)` with `SetDisplayRange(range)`.
  It accepts the requested mapping and does not await display. Observe subsequent
  rendering errors through `DisplayFailed`; snapshots use the last committed mapping.
- `await SubmitFrameAsync(...)` now completes reception only. Replace status
  switches using `Committed`, `Superseded` or `Failed` with the reception statuses
  `Accepted`, `Cancelled`, `Frozen` and `Closed`, and inspect `result.Error` for
  reception-owned cleanup failures. Enum numeric values changed; do not reuse
  persisted numeric statuses as if they had the old meaning.
- **`Accepted` does not mean displayed and never makes transferred storage reusable.**
  Accepted frames can be replaced without a display notification. Cancellation is
  checked only before acceptance. Observe successful UI commits with `FrameCommitted`
  or the submission's `OnCommitted` callback, and failures with `DisplayFailed`.
  Subscribe before submitting; a commit is not proof of physical monitor presentation.
  Do not wait unconditionally for every accepted frame to commit.
- `OnCommitted` receives a lease borrowed only until callback return; acquire an
  independent lease for later work. Recycle owned buffers only from their final-release
  callback, or use `ImageFrame.Copy` for borrowed/reusable producer buffers. Later
  cleanup failures for accepted storage are reported by `DisposeAsync`, not a newer receipt.
- For embedded use, create on the host STA and await `DisposeAsync` before its
  dispatcher stops. `Unloaded` ends interaction but preserves content for reattachment
  on the same dispatcher; it is not disposal. Final disposal is terminal and never
  closes the host window or dispatcher.

See [embedding](docs/en/embedding.md) ([中文](docs/zh-CN/embedding.md)),
[frame ownership](docs/en/frame-pipeline.md) ([中文](docs/zh-CN/frame-pipeline.md))
and [event contracts](docs/en/public-api.md#event-subscriptions).

## 1.2.0

This release contains breaking API changes. Rebuild consumers after updating the
calls below; binaries compiled against 1.1.2 are not guaranteed to be compatible.

### Changes

- Add `Rgba32` frame display, pixel queries and snapshot support.
- Harden frame ownership, shutdown, callbacks and drawing/input behavior.
- Make display-range updates awaitable and consolidate image-save menu options.
- Retain bilingual documentation and package-derived API references by version,
  with immutable release bundles and recoverable publication.
- Add the optional `Fizzy.ImageViewer.Cuda` execution bridge package, published
  separately with its matching Windows x64 native component.

### Upgrade from 1.1.2

- Replace writes to `Viewer.DisplayRange` / `IViewerDisplay.DisplayRange` with
  `await SetDisplayRangeAsync(range)` and observe failures from the returned task.
- Replace `ImageFrame.TakeD3D9Surface(...)` with
  `D3D9ImageFrame.TakeOwnership(...)`.
- Replace `FramePixelReader.Instance.TryRead(...)` and `ReadPixels(...)` with
  `CpuFramePixelReader.TryReadPixel(...)` and `ReadPixels(...)`.
- Rename `PixelQueryResult` to `PixelSamplesResult` and
  `MeasurementRegionResult` to `MeasurementRegionStatisticsResult`.
- Use `SourceFrame` instead of `Frame` on pixel, region-statistics and measurement
  query results. Update named constructor arguments accordingly.
- Obtain `RegionPixels` through `FrameLease.ReadRegionAsync`; its constructor is
  no longer public.
- Update named cancellation arguments from `ct:` to `cancellationToken:` and
  `PixelRegion` descriptor arguments from `d:` to `descriptor:`.

Review the [public API](docs/en/public-api.md) and
[frame ownership contracts](docs/en/frame-pipeline.md) when updating adapters.
