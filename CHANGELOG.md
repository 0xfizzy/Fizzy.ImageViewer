# Changelog

## 1.3.0 (2026-10-27)

This release contains breaking API and behavioral changes. Update and rebuild
consumers compiled against 1.2.0; binary compatibility is not guaranteed.

### Changes

- Add `ImageViewerControl` for embedding on a WPF host's dispatcher. `Viewer`
  hosts the same implementation on its independent window STA.
- Add borrowed `IViewerCapabilities` and `WhenStopping` observation, plus
  `EmbeddedViewer` and `ApplicationEndpoint` examples with awaited shutdown.
- Add `ImageBindingSource` and `ImageViewerControl.ImageSource` for static images
  and infrequent updates, with `ImageSourceSubmitResult` for per-control reception
  feedback. Live streams continue to use explicit `SubmitFrameAsync`.
- Add routed `ImageViewerCommands.FitToViewport`, `ActivateMeasurementTool` and
  `EndInteraction` commands with per-control availability and explicit targets.
- Separate frame reception from display. Hidden, unloaded and minimized hosts
  retain the latest waiting frame and resume display without requiring new input.
- Clip image, drawing, measurement and HUD content at the owned viewport boundary,
  including after zoom, pan and resize. Pointer input restores focus to the clicked
  viewer so keyboard interaction does not target a neighboring control.
- Add `PixelCoordinate.ToImageCenter()` for converting integer pixel indices to
  continuous image coordinates, and use high-quality image downscaling.
- Include .NET 10 and .NET 8 Windows assemblies and bilingual embedding guidance.

### Upgrade from 1.2.0

Update window/capability interfaces, event handlers and display-range calls; migrate
submission handling from display completion to reception. Preserve final-release
ownership and await shutdown. Follow the [upgrade guide](docs/en/upgrading.md)
([中文](docs/zh-CN/upgrading.md)) for before/after examples and validation steps.

Replace the former `Fizzy.ImageViewer.Cuda` package with exactly one of
`Fizzy.ImageViewer.Cuda11`, `.Cuda12` or `.Cuda13`, matching the host's CUDA major
version. Do not mix variants or retain the former package alongside one. The namespace
and assembly name remain `Fizzy.ImageViewer.Cuda`. Use matching 1.3.0 core/extension
versions when available: CUDA packages are published separately, so successful core
publication does not imply that the extensions are already available.

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
