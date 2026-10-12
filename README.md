<p align="center"><img src="assets/icon-readme.png" alt="Fizzy.ImageViewer" width="108" height="108"></p>

# Fizzy.ImageViewer

[![NuGet](https://img.shields.io/nuget/v/Fizzy.ImageViewer.svg?style=flat)](https://www.nuget.org/packages/Fizzy.ImageViewer/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg?style=flat)](LICENSE)
[![.NET 8 / 10](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4.svg?style=flat)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows-blue.svg?style=flat)](docs/en/getting-started.md)

English | [简体中文](README.zh-CN.md)

View, draw, measure, and inspect images in Windows/.NET 8 and .NET 10 WPF applications. Each Viewer owns an independent window and STA thread. Applications provide camera acquisition, device control, and business adapters.

Read the [documentation website](https://0xfizzy.github.io/Fizzy.ImageViewer/) for bilingual guides and the API reference.

## Install

```powershell
dotnet add package Fizzy.ImageViewer
```

The NuGet package includes `net10.0-windows` and `net8.0-windows` assemblies. Target either framework (or a compatible newer target), and set `<UseWPF>true</UseWPF>`.

## Quick start

With implicit usings enabled, this console entry point displays a gray image until you close the window:

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);
viewer.Closed += (_, _) => closed.TrySetResult();
var pixels = new byte[640 * 480];
Array.Fill(pixels, (byte)128);
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != FrameSubmitStatus.Committed)
    throw new InvalidOperationException($"Initial frame: {result.Status}");
viewer.Show();
await closed.Task;
```

Submission immediately transfers frame ownership, even on rejection or cancellation. `Committed` means the UI accepted the frame, not that the monitor has displayed it.

## Features

- Zoom, pan, fit-to-window, and borderless display
- Shape overlays, batched markers, and screen-space HUD text
- Point, length, rectangle ROI, and line-profile measurements with editing
- Original-pixel queries, image/region snapshots, and custom menus
- Bounded latest-frame submission and optional CUDA integration

## Documentation

| I want to… | Read |
| --- | --- |
| Run my first application | [Getting started](docs/en/getting-started.md) |
| Display a live stream | [Live streaming](docs/en/live-streaming.md) |
| Draw or measure | [Drawing and HUD](docs/en/drawing-layers.md) |
| Use interactive measurements | [Measurements](docs/en/measurements.md) |
| Find a component or API | [Guide index](docs/en/index.md) |

[Bilingual entry](docs/index.md)

## Development

See [validation](docs/en/validation.md) for build and test commands, [architecture](docs/en/architecture.md) for module boundaries, and [documentation maintenance](docs/en/documentation.md) or [release SOP](docs/en/releasing.md) for maintenance tasks.

## License

[MIT](LICENSE).
