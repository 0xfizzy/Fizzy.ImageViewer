# Changelog

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
