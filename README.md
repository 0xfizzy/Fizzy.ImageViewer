<p align="center">
  <img src="assets/icon-readme.png" alt="Fizzy.ImageViewer icon" width="160">
</p>

<h1 align="center">Fizzy.ImageViewer</h1>

<p align="center">
  <a href="#install">Install</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#features">Features</a> ·
  <a href="docs/frame-pipeline.md">Frame pipeline</a>
</p>

<p align="center">
  <strong>View, measure, and inspect images in WPF applications.</strong>
</p>

<p align="center">
  A fast, standalone image viewer for .NET 8 with zoom and pan, shape overlays,
  measurements, interactive editing, HUD content, and pixel inspection.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Fizzy.ImageViewer"><img src="https://img.shields.io/nuget/v/Fizzy.ImageViewer.svg" alt="NuGet version"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/.NET-8-512BD4.svg" alt=".NET 8">
  <img src="https://img.shields.io/badge/platform-WPF-0078D4.svg" alt="Platform: WPF">
</p>

## Install

```powershell
dotnet add package Fizzy.ImageViewer
```

The consuming project must target `net8.0-windows` (or a compatible newer target)
and set `<UseWPF>true</UseWPF>`.

## Quick start

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging.Abstractions;

await using var viewer = new Viewer(NullLogger<Viewer>.Instance);
var pixels = new byte[640 * 480];
var frame = ImageFrame.Copy(new FrameDescriptor(640, 480, 640,
    FramePixelFormat.Gray8), pixels);
var result = await viewer.SubmitFrameAsync(frame);
```

Submission transfers ownership immediately. Awaiting returns a commit/drop result,
not a physical presentation timestamp. One frame is processed while only the newest
waiting frame is retained. Measurements read original pixels independently of display.

For thousands of markers, submit a collection as one visual:

```csharp
using var markers = viewer.Layers.Markers.AddBatch(
    Enumerable.Range(0, 10000).Select(i =>
        new Fizzy.ImageViewer.Drawing.CircleElement(
            new System.Windows.Point(i % 100 * 10, i / 100 * 10),
            3, System.Windows.Media.Brushes.Red)));
viewer.StartMeasurement(MeasurementToolIds.Length); // Independent interactive measurement layer.
```

See [measurement ownership and interaction](docs/measurements.md).
See [supported public API and window lifetime](docs/public-api.md).
Application adapters own a sealed `Viewer`; use `showWindow: false` to configure and
subscribe before `Show()`, and await `DisposeAsync()` when the adapter stops.
See [drawing layers](docs/drawing-layers.md) for updates, hit testing, and API behavior.
For camera streams, retain batches and replace their content; follow the
[live overlay consumer requirements](docs/drawing-layers.md#live-camera-overlays-consumer-requirements).
See [frame, rendering and snapshot contracts](docs/frame-pipeline.md) and
[validation commands](docs/validation.md).

## Features

- Independent STA window with thread-safe API dispatch
- Low-allocation frame rendering and bounded render queue
- Zoom, pan, fit-to-window, and borderless modes
- Lines, text, crosshairs, rectangles, circles, and HUD text
- [Batch drawing and configurable layers](docs/drawing-layers.md): one visual per collection
- Built-in point, line, rectangle, and line-strength measurements
- Shape selection and interactive editing
- Model-driven custom measurements with shared editing, queries and notifications
- Extensible context menus
- Pixel value inspection

## Build

See [architecture and ownership](docs/architecture.md) before changing module boundaries.

```powershell
dotnet build -c Release
dotnet pack -c Release -o ./artifacts
```

Releases are published by GitHub Actions through NuGet Trusted Publishing.
The repository contains no NuGet API key. The NuGet.org trusted-publisher policy
must match owner `0xfizzy`, repository `Fizzy.ImageViewer`, and workflow
`.github/workflows/release.yml`; the repository variable `NUGET_USER` contains
the NuGet.org username. Pushing a `v*` tag starts the release workflow.

## License

MIT
