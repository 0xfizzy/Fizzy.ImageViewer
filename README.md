# Fizzy.ImageViewer

![Fizzy.ImageViewer icon](assets/icon.svg)

A fast, standalone WPF image viewer for .NET 8. It provides zoom and pan,
shape overlays, measurements, interactive editing, HUD content, and pixel inspection.

[![NuGet](https://img.shields.io/nuget/v/Fizzy.ImageViewer.svg)](https://www.nuget.org/packages/Fizzy.ImageViewer)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

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

See [frame, rendering and snapshot contracts](docs/frame-pipeline.md) and
[validation commands](docs/validation.md).
## Features

- Independent STA window with thread-safe API dispatch
- Low-allocation frame rendering and bounded render queue
- Zoom, pan, fit-to-window, and borderless modes
- Lines, text, crosshairs, rectangles, circles, and HUD text
- Built-in point, line, rectangle, and line-strength measurements
- Shape selection and interactive editing
- Extensible context menu and measurement APIs
- Pixel value inspection

## Build

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
